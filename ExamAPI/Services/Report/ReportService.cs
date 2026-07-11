using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
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

        private static SubjectMarksDto? BuildSubjectMarks(IEnumerable<StudentMarks> studentMarks)
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
                GradePoint = marksList.Select(sm => sm.GradePoint).FirstOrDefault(point => point.HasValue) ?? 0,
                Grade = marksList.Select(sm => sm.Grade).FirstOrDefault(grade => !string.IsNullOrWhiteSpace(grade)) ?? "F"
            };

            foreach (var mark in marksList)
            {
                var configuredHead = mark.CreditMaster?.Credits?.FirstOrDefault(head =>
                    string.Equals(head.Head, mark.Head, StringComparison.OrdinalIgnoreCase));

                subjectDto.Heads.Add(new HeadMarksDto
                {
                    Head = mark.Head ?? "Unspecified",
                    Max = double.TryParse(configuredHead?.HeadOutOf, out var max) ? max : 0,
                    Marks = mark.Marks?.ToString() ?? "AB",
                    Grace = new string((mark.Grace ?? string.Empty).Where(character => !char.IsDigit(character)).ToArray())
                });
            }

            subjectDto.TotalMax = subjectDto.Heads.Sum(head => head.Max);
            subjectDto.PassingMax = marksList.Sum(mark =>
            {
                var configuredHead = mark.CreditMaster?.Credits?.FirstOrDefault(head =>
                    string.Equals(head.Head, mark.Head, StringComparison.OrdinalIgnoreCase));
                return double.TryParse(configuredHead?.HeadPass, out var pass) ? pass : 0;
            });
            subjectDto.TotalObtained = marksList.Sum(mark => mark.Marks ?? 0).ToString();

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
            
            if (!exam.IsLocked)
            {
                exam.IsLocked = true;
                await _context.SaveChangesAsync();
            }
            
            var ruleSet = await _context.RuleSets
                .Include(rs => rs.GradeMaster)
                    .ThenInclude(gm => gm!.Thresholds)
                .FirstOrDefaultAsync(rs => rs.Pattern!.PatternName == request.Pattern
                    && rs.Pattern.CollegeId == collegeId
                    && rs.IsActive
                    && !rs.IsDeleted);

            var gradeMaster = ruleSet?.GradeMaster;

            var college = await _context.Colleges.FirstOrDefaultAsync(c => c.CollegeId == collegeId);
            var collegeName = college?.Name ?? "College Name Not Found";

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
                .Include(m => m.Exam)
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
                    var subDto = BuildSubjectMarks(group);
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

            // Add Headers
            worksheet.Cells[1, 1].Value = "Seat No";
            worksheet.Cells[1, 2].Value = "Student Name";
            worksheet.Cells[1, 3].Value = "PRN";

            var reportSubjects = reportDto.Students
                .SelectMany(student => student.Subjects)
                .GroupBy(subject => subject.SubjectCode)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    SubjectCode = group.Key,
                    Heads = group
                        .SelectMany(subject => subject.Heads)
                        .Select(head => head.Head)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(head => head, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList();

            int col = 4;
            foreach (var subject in reportSubjects)
            {
                foreach (var head in subject.Heads)
                {
                    worksheet.Cells[1, col++].Value = $"{subject.SubjectCode} ({head})";
                }

                worksheet.Cells[1, col++].Value = $"{subject.SubjectCode} Total";
            }
            
            worksheet.Cells[1, col++].Value = "Total Marks";
            worksheet.Cells[1, col++].Value = "Credits";
            worksheet.Cells[1, col++].Value = "SGPI";
            if (reportDto.ShowCgpi)
            {
                worksheet.Cells[1, col++].Value = "CGPI";
            }
            worksheet.Cells[1, col].Value = "Remark";

            string FormatNumber(double value)
            {
                return request.RoundNumber
                    ? Math.Round(value, MidpointRounding.AwayFromZero).ToString()
                    : value.ToString("0.##");
            }

            // Add Data
            int row = 2;
            foreach (var student in reportDto.Students)
            {
                var hasFailure = student.Subjects.Any(subject => string.Equals(subject.Grade, "F", StringComparison.OrdinalIgnoreCase));
                var displayRemark = hasFailure && request.NoRleForFail && string.Equals(student.Remark, "RLE", StringComparison.OrdinalIgnoreCase)
                    ? "Fail"
                    : student.Remark;
                var sgpi = hasFailure && !request.SgpiForFail ? "--" : FormatNumber(student.SGPI);
                var cgpi = hasFailure && !request.CgpiForFail ? "--" : FormatNumber(student.CGPI ?? 0);

                worksheet.Cells[row, 1].Value = student.SeatNo;
                worksheet.Cells[row, 2].Value = student.StudentName;
                worksheet.Cells[row, 3].Value = student.PRN;
                
                int dataCol = 4;
                foreach (var subjectColumn in reportSubjects)
                {
                    var subject = student.Subjects.FirstOrDefault(sub =>
                        string.Equals(sub.SubjectCode, subjectColumn.SubjectCode, StringComparison.OrdinalIgnoreCase));

                    foreach (var headName in subjectColumn.Heads)
                    {
                        var head = subject?.Heads.FirstOrDefault(item =>
                            string.Equals(item.Head, headName, StringComparison.OrdinalIgnoreCase));
                        worksheet.Cells[row, dataCol++].Value = head == null ? "-" : $"{head.Marks}{head.Grace}";
                    }

                    if (subject != null && double.TryParse(subject.TotalObtained, out var subTotal))
                    {
                        worksheet.Cells[row, dataCol++].Value = FormatNumber(subTotal);
                    }
                    else
                    {
                        worksheet.Cells[row, dataCol++].Value = subject?.TotalObtained ?? "-";
                    }
                }
                
                worksheet.Cells[row, dataCol++].Value = FormatNumber(student.TotalObtained);
                worksheet.Cells[row, dataCol++].Value = FormatNumber(student.CreditsEarned);
                worksheet.Cells[row, dataCol++].Value = sgpi;
                if (reportDto.ShowCgpi)
                {
                    worksheet.Cells[row, dataCol++].Value = cgpi;
                }
                worksheet.Cells[row, dataCol].Value = displayRemark;
                row++;
            }

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

            var programName = exam?.Course?.Name ?? "N/A";
            if (programName == "CS & E(DS)") programName = "Computer Science & Engineering (Data Science)";
            else if (programName == "CS & E") programName = "Computer Science & Engineering";

            var examName = exam?.Name ?? "Regular Exam";
            if (marksMaster.Exam?.ExamType == "KT" || marksMaster.Exam?.ExamType == "ATKT" || exam?.ExamType == "KT" || exam?.ExamType == "ATKT") 
            {
                if (!examName.Contains("(ATKT)")) examName += " (ATKT)";
            }

            var subjectGroups = marksMaster.StudentMarks?.Where(sm => !sm.IsDeleted).GroupBy(sm => sm.SubjectId) ?? Enumerable.Empty<IGrouping<Guid?, StudentMarks>>();
            
            var hasFailure = subjectGroups.Any(group => group.FirstOrDefault()?.GradePoint == 0);
            var displayRemark = marksMaster.ResultRemark ?? marksMaster.OverallRemark ?? "Pending";
            if (hasFailure && noRleForFail && string.Equals(displayRemark, "RLE", StringComparison.OrdinalIgnoreCase))
            {
                displayRemark = "Fail";
            }

            var reportDto = new MarksheetReportDto
            {
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
                var subDto = BuildSubjectMarks(group);
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
                .Where(m => m.ExamId == examId
                    && m.SemesterId == semId
                    && m.Pattern == pattern
                    && m.Student != null
                    && m.Student.CollegeId == collegeId
                    && !m.IsDeleted);

            if (generationType.Equals("pass", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(m => m.OverallRemark == "Pass" || m.OverallRemark == "SUCCESSFUL");
            }
            else if (generationType.Equals("fail", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(m => m.OverallRemark == "Fail" || m.OverallRemark == "UNSUCCESSFUL");
            }

            var marksMasters = await query.ToListAsync();

            if (!marksMasters.Any())
                throw new Exception("No results found for the given criteria. Have you processed the results yet?");

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
                
                var hasFailure = subjectGroups.Any(group => group.FirstOrDefault()?.GradePoint == 0);
                var displayRemark = marksMaster.ResultRemark ?? marksMaster.OverallRemark ?? "Pending";
                if (hasFailure && noRleForFail && string.Equals(displayRemark, "RLE", StringComparison.OrdinalIgnoreCase))
                {
                    displayRemark = "Fail";
                }

                var reportDto = new MarksheetReportDto
                {
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
                    var subDto = BuildSubjectMarks(group);
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
