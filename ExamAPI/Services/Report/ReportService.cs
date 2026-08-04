using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.Report.Documents;
using ExamAPI.Services.Result;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using QuestPDF.Fluent;

namespace ExamAPI.Services.Report
{
    public class ReportService : IReportService
    {
        private readonly IResultService _resultService;
        private readonly ApplicationDbContext _context;

        public ReportService(IResultService resultService, ApplicationDbContext context)
        {
            _resultService = resultService;
            _context = context;
        }

        /// <summary>The tenant's name as printed on every report header.</summary>
        private async Task<string> GetCollegeNameAsync(Guid collegeId)
        {
            var college = await _context.Colleges.FirstOrDefaultAsync(c => c.CollegeId == collegeId);
            return college?.Name ?? "College Name Not Found";
        }

        /// <summary>The computed verdict for the subject these head rows belong to.</summary>
        private static StudentSubjectResult? FindSubjectResult(MarksMaster marksMaster, IEnumerable<StudentMarks> group)
        {
            var subjectId = group.Select(sm => sm.SubjectId).FirstOrDefault(id => id.HasValue);
            return subjectId is Guid id
                ? marksMaster.SubjectResults?.FirstOrDefault(r => r.SubjectId == id)
                : null;
        }

        /// <summary>
        /// Builds one printed subject: head marks come from StudentMarks, but the verdict
        /// (grade, grade point, subject-level grace) comes from the computed StudentSubjectResult.
        /// </summary>
        private static SubjectMarksDto? BuildSubjectMarks(IEnumerable<StudentMarks> studentMarks, StudentSubjectResult? subjectResult)
        {
            var marksList = studentMarks
                .Where(sm => !sm.IsDeleted)
                .OrderBy(sm => sm.Head ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(sm => sm.Id)
                .ToList();

            var firstSm = marksList.FirstOrDefault();
            var subject = firstSm?.Subject;
            if (firstSm == null || subject == null)
            {
                return null;
            }

            var subjectDto = new SubjectMarksDto
            {
                SubjectCode = subject.SubjectCode,
                SubjectName = subject.Name,
                Credits = double.TryParse(firstSm.CreditMaster?.TotalCredits, out var credits) ? credits : 0,
                GradePoint = subjectResult?.GradePoint ?? 0,
                Grade = string.IsNullOrWhiteSpace(subjectResult?.Grade) ? "F" : subjectResult!.Grade!,
                Grace = subjectResult != null && subjectResult.GraceApplied > 0 ? subjectResult.GraceSymbol : null
            };

            foreach (var mark in marksList)
            {
                subjectDto.Heads.Add(new HeadMarksDto
                {
                    Head = SubjectPassEvaluator.GetHeadLabel(mark),
                    Max = SubjectPassEvaluator.GetHeadOutOf(mark),
                    Marks = mark.IsAbsent ? "AB" : mark.Marks?.ToString() ?? "",
                    Grace = new string((mark.Grace ?? string.Empty).Where(character => !char.IsDigit(character)).ToArray())
                });
            }

            subjectDto.TotalMax = subjectDto.Heads.Sum(head => head.Max);
            subjectDto.PassingMax = marksList.Sum(SubjectPassEvaluator.GetHeadPass);
            subjectDto.TotalObtained = (subjectResult?.ObtainedTotal ?? marksList.Sum(mark => mark.Marks ?? 0)).ToString();

            return subjectDto;
        }

        private async Task<GazetteReportDto> GetGazetteDataAsync(GazetteRequestDto request, Guid collegeId)
        {
            var exam = await _context.Exams
                .Include(e => e.Course)
                .FirstOrDefaultAsync(e => e.ExamId == request.ExamId
                    && e.Course != null
                    && e.Course.CollegeId == collegeId
                    && !e.IsDeleted);

            if (exam == null)
            {
                throw new InvalidOperationException("The selected exam is unavailable for the current college.");
            }

            // Commented out to prevent automatic exam locking during testing/run
            // if (!exam.IsLocked)
            // {
            //     exam.IsLocked = true;
            //     await _context.SaveChangesAsync();
            // }

            var ruleSet = await _context.RuleSets
                .Include(rs => rs.GradeMaster)
                    .ThenInclude(gm => gm!.Thresholds)
                .FirstOrDefaultAsync(rs => rs.Pattern!.PatternName == request.Pattern
                    && rs.Pattern.CollegeId == collegeId
                    && rs.IsActive
                    && !rs.IsDeleted);

            var gradeMaster = ruleSet?.GradeMaster;

            var collegeName = await GetCollegeNameAsync(collegeId);

            var programName = exam?.Course?.Name ?? "N/A";
            if (programName == "CS & E(DS)") programName = "Computer Science & Engineering (Data Science)";
            else if (programName == "CS & E") programName = "Computer Science & Engineering";

            var reportDto = new GazetteReportDto
            {
                CollegeName = collegeName,
                ProgramName = programName,
                Semester = $"Semester {request.SemId}",
                ExamName = exam?.Name ?? "Regular Exam",
                ResultDate = DateTime.Now,
                ShowCgpi = request.CgpiForFail,
                Students = new List<StudentResultSummaryDto>()
            };

            var query = _context.MarksMasters
                .Include(m => m.Student)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.Subject)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.CreditMaster)
                        .ThenInclude(cm => cm!.Credits)
                .Include(m => m.SubjectResults)
                .Include(m => m.Exam)
                // Sibling collection Includes would otherwise be one JOIN whose row count is their
                // product; split queries keep the payload linear (the DB is a remote link).
                .AsSplitQuery()
                .Where(m => !m.IsDeleted
                    && m.SemesterId == request.SemId
                    && m.Pattern == request.Pattern
                    && m.Student != null
                    && m.Student.CollegeId == collegeId);

            if (request.MergeExam && request.MergedExamId.HasValue)
            {
                query = query.Where(m => m.ExamId == request.ExamId || m.ExamId == request.MergedExamId.Value);
            }
            else
            {
                query = query.Where(m => m.ExamId == request.ExamId);
            }

            var marksMasters = await query.ToListAsync();

            var groupedByStudent = marksMasters.GroupBy(m => m.StdMstId).ToList();

            Dictionary<Guid, List<SemesterRecordDto>> bulkHistory = new();
            if (request.CxgSems.Any() || request.GpaSems.Any())
            {
                var studentIds = groupedByStudent.Select(g => g.Key).Where(id => id.HasValue).Select(id => id!.Value).ToList();
                if (studentIds.Any())
                {
                    bulkHistory = await GetBulkSemesterHistoryAsync(studentIds);
                }
            }

            foreach (var studentGroup in groupedByStudent)
            {
                var marksMaster = studentGroup.FirstOrDefault(m => m.ExamId == request.ExamId) ?? studentGroup.First();

                var student = marksMaster.Student;
                var studentName = student != null ? $"{student.FirstName} {student.LastName}" : "N/A";
                if (marksMaster.QuotaType == "LD") studentName = "~" + studentName;

                double? calculatedCgpi = marksMaster.CGPI.HasValue ? (double)marksMaster.CGPI.Value : null;

                if ((request.CxgSems.Any() || request.GpaSems.Any()) && marksMaster.StdMstId.HasValue && bulkHistory.ContainsKey(marksMaster.StdMstId.Value))
                {
                    var history = bulkHistory[marksMaster.StdMstId.Value];

                    if (request.CxgSems.Any())
                    {
                        var relevantSems = history.Where(h =>
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(h.SemesterName, @"\d+");
                            return match.Success && request.CxgSems.Contains(int.Parse(match.Value));
                        }).ToList();

                        var cxgTotalCredits = relevantSems.Sum(h => h.Credits);
                        var cxgTotalEarnedGradePoints = relevantSems.Sum(h => h.EarnedGradePoints);

                        if (cxgTotalCredits > 0)
                        {
                            calculatedCgpi = (double)(cxgTotalEarnedGradePoints / cxgTotalCredits);
                        }
                    }
                    else if (request.GpaSems.Any())
                    {
                        var relevantSems = history.Where(h =>
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(h.SemesterName, @"\d+");
                            return match.Success && request.GpaSems.Contains(int.Parse(match.Value));
                        }).ToList();

                        if (relevantSems.Any())
                        {
                            calculatedCgpi = (double)relevantSems.Average(h => h.SGPI);
                        }
                    }
                }

                var studentDto = new StudentResultSummaryDto
                {
                    StudentId = marksMaster.StudentID ?? "N/A",
                    SeatNo = marksMaster.SeatNo ?? "N/A",
                    StudentName = studentName,
                    PRN = student?.StudentPRN ?? "N/A",
                    Remark = marksMaster.ResultRemark ?? marksMaster.OverallRemark ?? "Pending",
                    SGPI = (double)(marksMaster.SGPI ?? 0),
                    CGPI = calculatedCgpi,
                    Subjects = new List<SubjectMarksDto>()
                };

                var allMarks = studentGroup.SelectMany(m => m.StudentMarks ?? Enumerable.Empty<StudentMarks>())
                                           .Where(sm => !sm.IsDeleted)
                                           .ToList();

                var subjectGroups = allMarks
                    .GroupBy(sm => sm.SubjectId)
                    .Select(sg => sg
                        .GroupBy(sm => sm.Head)
                        .Select(hg => hg.OrderByDescending(sm => sm.Marks ?? 0).ThenByDescending(sm => sm.UpdatedAt).First())
                        .ToList()
                    ).ToList();

                double totalObtained = 0;
                double totalMax = 0;
                double totalCredits = 0;
                double creditsEarned = 0;

                foreach (var group in subjectGroups)
                {
                    var subDto = BuildSubjectMarks(group, FindSubjectResult(marksMaster, group));
                    if (subDto == null) continue;

                    studentDto.Subjects.Add(subDto);

                    totalObtained += double.TryParse(subDto.TotalObtained, out var obtained) ? obtained : 0;
                    totalMax += subDto.TotalMax;
                    totalCredits += subDto.Credits;
                    if (subDto.GradePoint > 0) creditsEarned += subDto.Credits;
                }

                studentDto.TotalObtained = totalObtained;
                studentDto.TotalMax = totalMax;
                studentDto.TotalCredits = totalCredits;
                studentDto.CreditsEarned = creditsEarned;
                studentDto.CumulativeGrade = studentDto.Subjects.Sum(subject => subject.EarnedGradePoints);

                reportDto.Students.Add(studentDto);
            }

            return reportDto;
        }

        public async Task<byte[]> GenerateGazettePdfAsync(GazetteRequestDto request, Guid collegeId)
        {
            var reportDto = await GetGazetteDataAsync(request, collegeId);
            var document = new GazetteDocument(reportDto, request);
            return document.GeneratePdf();
        }

        public async Task<byte[]> GenerateGazetteExcelAsync(GazetteRequestDto request, Guid collegeId)
        {
            var reportDto = await GetGazetteDataAsync(request, collegeId);

            ExcelPackage.License.SetNonCommercialPersonal("ReactApi Project");
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Gazette");
            worksheet.Cells.Style.Font.Name = "Arial";
            worksheet.Cells.Style.Font.Size = 8;

            var studentsPerPage = Math.Clamp(request.StudentsPerPage, 1, 4);
            var subjectsPerRow = Math.Clamp(request.SubjectsPerRow, 1, 6);
            var studentChunks = reportDto.Students.Chunk(studentsPerPage).ToList();

            int totalColumns = 1 + subjectsPerRow + 3 + (reportDto.ShowCgpi ? 1 : 0) + 1;

            worksheet.PrinterSettings.Orientation = eOrientation.Landscape;
            worksheet.PrinterSettings.PaperSize = ePaperSize.A4; // Match PDF A4 Landscape
            worksheet.PrinterSettings.LeftMargin = 0.2;
            worksheet.PrinterSettings.RightMargin = 0.2;
            worksheet.PrinterSettings.TopMargin = 0.2;
            worksheet.PrinterSettings.BottomMargin = 0.2;
            worksheet.PrinterSettings.HeaderMargin = 0.0;
            worksheet.PrinterSettings.FooterMargin = 0.0;
            worksheet.PrinterSettings.FitToPage = true;
            worksheet.PrinterSettings.FitToWidth = 1;
            worksheet.PrinterSettings.FitToHeight = 0;
            worksheet.PrinterSettings.RepeatRows = new ExcelAddress("1:5"); // Rows 1 to 5 repeat on every printed page

            int currentRow = 1;

            // Header: College Name
            worksheet.Cells[currentRow, 1, currentRow, totalColumns].Merge = true;
            worksheet.Cells[currentRow, 1].Value = reportDto.CollegeName;
            worksheet.Cells[currentRow, 1].Style.Font.Bold = true;
            worksheet.Cells[currentRow, 1].Style.Font.Size = 18;
            worksheet.Cells[currentRow, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            currentRow++;

            // Header: Program & Date
            int halfCols = Math.Max(totalColumns / 2, 1);
            worksheet.Cells[currentRow, 1, currentRow, halfCols].Merge = true;
            worksheet.Cells[currentRow, 1].Value = $"Program Name: {reportDto.ProgramName}";
            worksheet.Cells[currentRow, 1].Style.Font.Bold = true;
            worksheet.Cells[currentRow, 1].Style.Font.Size = 12;

            worksheet.Cells[currentRow, halfCols + 1, currentRow, totalColumns].Merge = true;
            worksheet.Cells[currentRow, halfCols + 1].Value = $"Result Date : {reportDto.ResultDate:dd/MM/yyyy}";
            worksheet.Cells[currentRow, halfCols + 1].Style.Font.Bold = true;
            worksheet.Cells[currentRow, halfCols + 1].Style.Font.Size = 12;
            worksheet.Cells[currentRow, halfCols + 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
            currentRow++;

            // Header: Semester & Exam
            worksheet.Cells[currentRow, 1, currentRow, halfCols].Merge = true;
            worksheet.Cells[currentRow, 1].Value = $"{reportDto.Semester}";
            worksheet.Cells[currentRow, 1].Style.Font.Bold = true;
            worksheet.Cells[currentRow, 1].Style.Font.Size = 12;

            worksheet.Cells[currentRow, halfCols + 1, currentRow, totalColumns].Merge = true;
            worksheet.Cells[currentRow, halfCols + 1].Value = $"Exam: {reportDto.ExamName}";
            worksheet.Cells[currentRow, halfCols + 1].Style.Font.Bold = true;
            worksheet.Cells[currentRow, halfCols + 1].Style.Font.Size = 12;
            worksheet.Cells[currentRow, halfCols + 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;
            currentRow++;

            currentRow++; // Empty row

            int headerRow = currentRow;
            // Add Table Headers
            worksheet.Cells[headerRow, 1].Value = "Student Details";
            for (int i = 0; i < subjectsPerRow; i++)
            {
                worksheet.Cells[headerRow, 2 + i].Value = "SubCode\nHead types\nMin/Max";
            }
            int col = 2 + subjectsPerRow;
            worksheet.Cells[headerRow, col++].Value = "Obt/Tot";
            worksheet.Cells[headerRow, col++].Value = "CG\nCE";
            worksheet.Cells[headerRow, col++].Value = "SGPA";
            if (reportDto.ShowCgpi)
            {
                worksheet.Cells[headerRow, col++].Value = "CGPI";
            }
            worksheet.Cells[headerRow, col].Value = "Remark";

            worksheet.Row(headerRow).Height = 45;

            using (var range = worksheet.Cells[headerRow, 1, headerRow, totalColumns])
            {
                range.Style.Font.Bold = true;
                range.Style.WrapText = true;
                range.Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
                range.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                range.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                range.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }

            string FormatNumber(double value)
            {
                return request.RoundNumber
                    ? Math.Round(value, MidpointRounding.AwayFromZero).ToString()
                    : value.ToString("0.##");
            }

            currentRow++;

            var distinctSubjects = reportDto.Students
                .SelectMany(s => s.Subjects)
                .GroupBy(s => s.SubjectCode)
                .Select(g => g.First())
                .OrderBy(s => s.SubjectCode)
                .ToList();

            foreach (var chunk in studentChunks)
            {
                foreach (var student in chunk)
                {
                    var subjectRows = student.Subjects.Chunk(subjectsPerRow).ToList();
                    if (subjectRows.Count == 0)
                    {
                        subjectRows.Add(Array.Empty<SubjectMarksDto>());
                    }

                    int startRow = currentRow;
                    int rowSpan = subjectRows.Count;

                    for (int rowIndex = 0; rowIndex < subjectRows.Count; rowIndex++)
                    {
                        var subjectRow = subjectRows[rowIndex];
                        var isFirstRow = rowIndex == 0;
                        var hasFailure = student.Subjects.Any(subject => string.Equals(subject.Grade, "F", StringComparison.OrdinalIgnoreCase));
                        var displayRemark = hasFailure && request.NoRleForFail && string.Equals(student.Remark, "RLE", StringComparison.OrdinalIgnoreCase)
                            ? OverallRemarks.Fail
                            : student.Remark;
                        var sgpi = hasFailure && !request.SgpiForFail ? "--" : FormatNumber(student.SGPI);
                        var cgpi = hasFailure && !request.CgpiForFail ? "--" : FormatNumber(student.CGPI ?? 0);

                        if (isFirstRow)
                        {
                            worksheet.Cells[startRow, 1].Value = $"{student.StudentName}\nSeat No: {student.SeatNo}\nPRN: {student.PRN}";

                            int trailingCol = 2 + subjectsPerRow;
                            worksheet.Cells[startRow, trailingCol++].Value = $"{FormatNumber(student.TotalObtained)}/{FormatNumber(student.TotalMax)}";
                            worksheet.Cells[startRow, trailingCol++].Value = $"{FormatNumber(student.CumulativeGrade ?? 0)} / {FormatNumber(student.CreditsEarned)}";
                            worksheet.Cells[startRow, trailingCol++].Value = sgpi;
                            if (reportDto.ShowCgpi) worksheet.Cells[startRow, trailingCol++].Value = cgpi;
                            worksheet.Cells[startRow, trailingCol].Value = displayRemark;
                        }

                        for (int i = 0; i < subjectRow.Length; i++)
                        {
                            var sub = subjectRow[i];
                            var subLines = new List<string> { sub.SubjectCode };
                            foreach (var head in sub.Heads)
                            {
                                subLines.Add($"{head.Head}: {head.Marks}{head.Grace}/{FormatNumber(head.Max)}");
                            }

                            subLines.Add($"C: {FormatNumber(sub.Credits)}  G: {sub.Grade}{sub.Grace}");
                            subLines.Add($"GP: {FormatNumber(sub.GradePoint)}  CG: {FormatNumber(sub.EarnedGradePoints)}");

                            worksheet.Cells[currentRow, 2 + i].Value = string.Join("\n", subLines);
                        }

                        currentRow++;
                    }

                    if (rowSpan > 1)
                    {
                        worksheet.Cells[startRow, 1, startRow + rowSpan - 1, 1].Merge = true;

                        int trailingCol = 2 + subjectsPerRow;
                        for (int i = 0; i < (totalColumns - 1 - subjectsPerRow); i++)
                        {
                            worksheet.Cells[startRow, trailingCol + i, startRow + rowSpan - 1, trailingCol + i].Merge = true;
                        }
                    }

                    using (var range = worksheet.Cells[startRow, 1, startRow + rowSpan - 1, totalColumns])
                    {
                        range.Style.WrapText = true;
                        range.Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Top;
                        range.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;

                        // Center align horizontally for subject columns and trailing columns
                        worksheet.Cells[startRow, 2, startRow + rowSpan - 1, totalColumns].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    }

                    // Keep Student Details and the Trailing summary columns vertically centered
                    worksheet.Cells[startRow, 1, startRow + rowSpan - 1, 1].Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
                    worksheet.Cells[startRow, 2 + subjectsPerRow, startRow + rowSpan - 1, totalColumns].Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
                }

                // Add legends at the end of each page chunk
                currentRow++;
                var subjectText = string.Join("  |  ", distinctSubjects.Select(s => $"{s.SubjectCode}: {s.SubjectName ?? "-"}"));
                worksheet.Cells[currentRow, 1, currentRow, totalColumns].Merge = true;
                worksheet.Cells[currentRow, 1].Value = "Subjects: " + subjectText;
                worksheet.Cells[currentRow, 1].Style.WrapText = true;
                worksheet.Cells[currentRow, 1].Style.Font.Size = 8;
                currentRow++;

                var abbrText = "C: Credits  |  G: Grade  |  GP: Grade Point  |  CG: Credits * Grade Point  |  CE: Credits Earned  |  SGPA: Semester Grade Point Average  |  CGPI: Cumulative Grade Point Index  |  --: Not Applicable  |  F: Fail  |  AB: Absent";
                worksheet.Cells[currentRow, 1, currentRow, totalColumns].Merge = true;
                worksheet.Cells[currentRow, 1].Value = "Abbreviations: " + abbrText;
                worksheet.Cells[currentRow, 1].Style.WrapText = true;
                worksheet.Cells[currentRow, 1].Style.Font.Size = 8;
                currentRow++;

                if (chunk != studentChunks.Last())
                {
                    worksheet.Row(currentRow - 1).PageBreak = true;
                }
            }

            // Adjust column widths explicitly
            worksheet.Column(1).Width = 24; // Student Details
            for (int i = 0; i < subjectsPerRow; i++)
            {
                worksheet.Column(2 + i).Width = 14; // Subjects
            }
            int c = 2 + subjectsPerRow;
            worksheet.Column(c++).Width = 9; // Obt/Tot
            worksheet.Column(c++).Width = 9; // CG/CE
            worksheet.Column(c++).Width = 8; // SGPA
            if (reportDto.ShowCgpi) worksheet.Column(c++).Width = 8; // CGPI
            worksheet.Column(c).Width = 12; // Remark

            return await package.GetAsByteArrayAsync();
        }

        public async Task<byte[]> GenerateMarksheetPdfAsync(Guid studId, Guid examId, string semId, string pattern, bool includeHistory, DateTime? resultDate, Guid collegeId, bool noRleForFail = false)
        {
            var exam = await _context.Exams
                .Include(e => e.Course)
                .FirstOrDefaultAsync(e => e.ExamId == examId
                    && e.Course != null
                    && e.Course.CollegeId == collegeId
                    && !e.IsDeleted);

            var ruleSet = await _context.RuleSets
                .Include(rs => rs.GradeMaster)
                    .ThenInclude(gm => gm!.Thresholds)
                .FirstOrDefaultAsync(rs => rs.Pattern!.PatternName == pattern
                    && rs.Pattern.CollegeId == collegeId
                    && rs.IsActive
                    && !rs.IsDeleted);
            var gradeMaster = ruleSet?.GradeMaster;

            var marksMaster = await _context.MarksMasters
                .Include(m => m.Student)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.Subject)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.CreditMaster)
                        .ThenInclude(cm => cm!.Credits)
                // Without this the subject verdict is missing and BuildSubjectMarks falls back
                // to "F"/GP 0 for every subject, drops the grace symbol, and prints the raw
                // (pre-grace) total. Same include as the gazette query.
                .Include(m => m.SubjectResults)
                .AsSplitQuery()
                .FirstOrDefaultAsync(m => m.StdMstId == studId
                    && m.ExamId == examId
                    && m.SemesterId == semId
                    && m.Pattern == pattern
                    && m.Student != null
                    && m.Student.CollegeId == collegeId
                    && !m.IsDeleted);

            if (marksMaster == null)
                throw new Exception("Result not found for the given student and exam. Have you processed the results yet?");

            var student = marksMaster.Student;

            var collegeName = await GetCollegeNameAsync(collegeId);
            var programName = exam?.Course?.Name ?? "N/A";
            if (programName == "CS & E(DS)") programName = "Computer Science & Engineering (Data Science)";
            else if (programName == "CS & E") programName = "Computer Science & Engineering";

            var examName = exam?.Name ?? "Regular Exam";
            if (marksMaster.Exam?.ExamType == "KT" || marksMaster.Exam?.ExamType == "ATKT" || exam?.ExamType == "KT" || exam?.ExamType == "ATKT")
            {
                if (!examName.Contains("(ATKT)")) examName += " (ATKT)";
            }

            var subjectGroups = marksMaster.StudentMarks?.Where(sm => !sm.IsDeleted).GroupBy(sm => sm.SubjectId) ?? Enumerable.Empty<IGrouping<Guid?, StudentMarks>>();

            var hasFailure = marksMaster.SubjectResults?.Any(r => !r.IsPassed) ?? false;
            var displayRemark = marksMaster.ResultRemark ?? marksMaster.OverallRemark ?? "Pending";
            if (hasFailure && noRleForFail && string.Equals(displayRemark, "RLE", StringComparison.OrdinalIgnoreCase))
            {
                displayRemark = OverallRemarks.Fail;
            }

            var reportDto = new MarksheetReportDto
            {
                CollegeName = collegeName,
                StudentName = student != null ? ((marksMaster.QuotaType == "LD" ? "~" : "") + $"{student.FirstName} {student.LastName}") : "N/A",
                SeatNo = marksMaster.SeatNo ?? "N/A",
                PRN = student?.StudentPRN ?? "N/A",
                StudentId = marksMaster.StudentID ?? "N/A",
                ProgramName = programName,
                ExamName = examName,
                Semester = $"Semester {semId}",
                ResultDate = resultDate?.Date ?? DateTime.Today,
                SGPI = (double)(marksMaster.SGPI ?? 0),
                CGPI = marksMaster.CGPI.HasValue ? (double)marksMaster.CGPI.Value : null,
                Remark = displayRemark,
                Subjects = new List<SubjectMarksDto>()
            };

            if (includeHistory)
            {
                reportDto.PastSemesters = await GetSemesterHistoryAsync(studId);
            }

            double totalObtained = 0;
            double totalMax = 0;
            double totalCredits = 0;
            double creditsEarned = 0;

            foreach (var group in subjectGroups)
            {
                var subDto = BuildSubjectMarks(group, FindSubjectResult(marksMaster, group));
                if (subDto == null) continue;

                reportDto.Subjects.Add(subDto);

                totalObtained += double.TryParse(subDto.TotalObtained, out var obtained) ? obtained : 0;
                totalMax += subDto.TotalMax;
                totalCredits += subDto.Credits;
                if (subDto.GradePoint > 0) creditsEarned += subDto.Credits;
            }

            reportDto.TotalObtained = totalObtained;
            reportDto.TotalMax = totalMax;
            reportDto.TotalCredits = totalCredits;
            reportDto.CreditsEarned = creditsEarned;
            reportDto.CumulativeGrade = reportDto.Subjects.Sum(subject => subject.EarnedGradePoints);

            var document = new MarksheetDocument(reportDto);
            return document.GeneratePdf();
        }

        public async Task<byte[]> GenerateBulkMarksheetPdfAsync(Guid examId, string semId, string pattern, string generationType, bool includeHistory, DateTime? resultDate, Guid collegeId, bool noRleForFail = false)
        {
            var exam = await _context.Exams
                .Include(e => e.Course)
                .FirstOrDefaultAsync(e => e.ExamId == examId
                    && e.Course != null
                    && e.Course.CollegeId == collegeId
                    && !e.IsDeleted);

            var ruleSet = await _context.RuleSets
                .Include(rs => rs.GradeMaster)
                    .ThenInclude(gm => gm!.Thresholds)
                .FirstOrDefaultAsync(rs => rs.Pattern!.PatternName == pattern
                    && rs.Pattern.CollegeId == collegeId
                    && rs.IsActive
                    && !rs.IsDeleted);
            var gradeMaster = ruleSet?.GradeMaster;

            var query = _context.MarksMasters
                .Include(m => m.Student)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.Subject)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.CreditMaster)
                        .ThenInclude(cm => cm!.Credits)
                // See the single-marksheet query: the subject verdict lives on SubjectResults,
                // and without it every subject prints as "F" with no grace.
                .Include(m => m.SubjectResults)
                // Sibling collection Includes would otherwise be one JOIN whose row count is their
                // product; split queries keep the payload linear (the DB is a remote link).
                .AsSplitQuery()
                .Where(m => m.ExamId == examId
                    && m.SemesterId == semId
                    && m.Pattern == pattern
                    && m.Student != null
                    && m.Student.CollegeId == collegeId
                    && !m.IsDeleted);

            if (generationType.Equals("pass", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(m => m.OverallRemark == OverallRemarks.Pass);
            }
            else if (generationType.Equals("fail", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(m => m.OverallRemark == OverallRemarks.Fail);
            }

            var marksMasters = await query.ToListAsync();

            if (!marksMasters.Any())
                throw new Exception("No results found for the given criteria. Have you processed the results yet?");

            var collegeName = await GetCollegeNameAsync(collegeId);
            var reports = new List<MarksheetReportDto>();

            Dictionary<Guid, List<SemesterRecordDto>> bulkHistory = new();
            if (includeHistory)
            {
                var studentIds = marksMasters.Select(m => m.Student?.StdMstId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
                if (studentIds.Any())
                {
                    bulkHistory = await GetBulkSemesterHistoryAsync(studentIds);
                }
            }

            foreach (var marksMaster in marksMasters)
            {
                var student = marksMaster.Student;
                var programName = exam?.Course?.Name ?? "N/A";
                if (programName == "CS & E(DS)") programName = "Computer Science & Engineering (Data Science)";
                else if (programName == "CS & E") programName = "Computer Science & Engineering";

                var examName = exam?.Name ?? "Regular Exam";
                if (marksMaster.Exam?.ExamType == "KT" || marksMaster.Exam?.ExamType == "ATKT" || exam?.ExamType == "KT" || exam?.ExamType == "ATKT")
                {
                    if (!examName.Contains("(ATKT)")) examName += " (ATKT)";
                }

                var subjectGroups = marksMaster.StudentMarks?.Where(sm => !sm.IsDeleted).GroupBy(sm => sm.SubjectId) ?? Enumerable.Empty<IGrouping<Guid?, StudentMarks>>();

                var hasFailure = marksMaster.SubjectResults?.Any(r => !r.IsPassed) ?? false;
                var displayRemark = marksMaster.ResultRemark ?? marksMaster.OverallRemark ?? "Pending";
                if (hasFailure && noRleForFail && string.Equals(displayRemark, "RLE", StringComparison.OrdinalIgnoreCase))
                {
                    displayRemark = OverallRemarks.Fail;
                }

                var reportDto = new MarksheetReportDto
                {
                    CollegeName = collegeName,
                    StudentName = student != null ? ((marksMaster.QuotaType == "LD" ? "~" : "") + $"{student.FirstName} {student.LastName}") : "N/A",
                    SeatNo = marksMaster.SeatNo ?? "N/A",
                    PRN = student?.StudentPRN ?? "N/A",
                    StudentId = marksMaster.StudentID ?? "N/A",
                    ProgramName = programName,
                    ExamName = examName,
                    Semester = $"Semester {semId}",
                    ResultDate = resultDate?.Date ?? DateTime.Today,
                    SGPI = (double)(marksMaster.SGPI ?? 0),
                    CGPI = marksMaster.CGPI.HasValue ? (double)marksMaster.CGPI.Value : null,
                    Remark = displayRemark,
                    Subjects = new List<SubjectMarksDto>()
                };

                if (includeHistory && student?.StdMstId != null && bulkHistory.ContainsKey(student.StdMstId))
                {
                    reportDto.PastSemesters = bulkHistory[student.StdMstId];
                }

                double totalObtained = 0;
                double totalMax = 0;
                double totalCredits = 0;
                double creditsEarned = 0;

                foreach (var group in subjectGroups)
                {
                    var subDto = BuildSubjectMarks(group, FindSubjectResult(marksMaster, group));
                    if (subDto == null) continue;

                    reportDto.Subjects.Add(subDto);

                    totalObtained += double.TryParse(subDto.TotalObtained, out var obtained) ? obtained : 0;
                    totalMax += subDto.TotalMax;
                    totalCredits += subDto.Credits;
                    if (subDto.GradePoint > 0) creditsEarned += subDto.Credits;
                }

                reportDto.TotalObtained = totalObtained;
                reportDto.TotalMax = totalMax;
                reportDto.TotalCredits = totalCredits;
                reportDto.CreditsEarned = creditsEarned;
                reportDto.CumulativeGrade = reportDto.Subjects.Sum(subject => subject.EarnedGradePoints);

                reports.Add(reportDto);
            }

            var document = new BulkMarksheetDocument(reports);
            return document.GeneratePdf();
        }

        private async Task<List<SemesterRecordDto>> GetSemesterHistoryAsync(Guid studentId)
        {
            var overallResults = await _context.StudentsOverallResults
                .Where(r => r.StdMstId == studentId && !r.IsDeleted)
                .ToListAsync();

            return overallResults
                .OrderBy(r =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(r.SemesterId ?? "", @"\d+");
                    return match.Success ? int.Parse(match.Value) : 0;
                })
                .Select(r => new SemesterRecordDto
                {
                    SemesterName = r.SemesterId ?? "",
                    Credits = double.TryParse(r.Credits, out var c) ? c : 0,
                    EarnedGradePoints = double.TryParse(r.CreditGradePoint, out var cg) ? cg : 0,
                    SGPI = r.SGPI.HasValue ? (double)r.SGPI.Value : 0
                })
                .ToList();
        }

        private async Task<Dictionary<Guid, List<SemesterRecordDto>>> GetBulkSemesterHistoryAsync(List<Guid> studentIds)
        {
            var overallResults = await _context.StudentsOverallResults
                .Where(r => r.StdMstId.HasValue && studentIds.Contains(r.StdMstId.Value) && !r.IsDeleted)
                .ToListAsync();

            var historyMap = new Dictionary<Guid, List<SemesterRecordDto>>();

            foreach (var studentGroup in overallResults.GroupBy(r => r.StdMstId!.Value))
            {
                var history = studentGroup
                    .OrderBy(r =>
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(r.SemesterId ?? "", @"\d+");
                        return match.Success ? int.Parse(match.Value) : 0;
                    })
                    .Select(r => new SemesterRecordDto
                    {
                        SemesterName = r.SemesterId ?? "",
                        Credits = double.TryParse(r.Credits, out var c) ? c : 0,
                        EarnedGradePoints = double.TryParse(r.CreditGradePoint, out var cg) ? cg : 0,
                        SGPI = r.SGPI.HasValue ? (double)r.SGPI.Value : 0
                    })
                    .ToList();
                historyMap[studentGroup.Key] = history;
            }

            return historyMap;
        }
    }
}
