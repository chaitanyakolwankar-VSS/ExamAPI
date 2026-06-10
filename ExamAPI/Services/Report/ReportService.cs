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

        private async Task<GazetteReportDto> GetGazetteDataAsync(GazetteRequestDto request)
        {
            var exam = await _context.Exams.Include(e => e.Course).FirstOrDefaultAsync(e => e.ExamId == request.ExamId);
            
            var ruleSet = await _context.RuleSets
                .Include(rs => rs.GradeMaster)
                    .ThenInclude(gm => gm!.Thresholds)
                .FirstOrDefaultAsync(rs => rs.Pattern!.PatternName == request.Pattern && rs.IsActive && !rs.IsDeleted);

            var gradeMaster = ruleSet?.GradeMaster;

            var reportDto = new GazetteReportDto
            {
                ProgramName = exam?.Course?.Name ?? "N/A", 
                Semester = $"Semester {request.SemId}", 
                ExamName = exam?.Name ?? "Regular Exam",
                ResultDate = DateTime.Now,
                ShowCgpi = request.CgpiForFail,
                Students = new List<StudentResultSummaryDto>()
            };

            var marksMasters = await _context.MarksMasters
                .Include(m => m.Student)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.Subject)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.CreditMaster)
                        .ThenInclude(cm => cm!.Credits)
                .Where(m => m.ExamId == request.ExamId && !m.IsDeleted)
                .ToListAsync();

            foreach (var marksMaster in marksMasters)
            {
                var student = marksMaster.Student;
                var studentDto = new StudentResultSummaryDto
                {
                    StudentId = marksMaster.StudentID ?? "N/A",
                    SeatNo = marksMaster.SeatNo ?? "N/A",
                    StudentName = student != null ? $"{student.FirstName} {student.LastName}" : "N/A",
                    PRN = student?.StudentPRN ?? "N/A",
                    Remark = marksMaster.OverallRemark ?? "Pending",
                    SGPI = (double)(marksMaster.SGPI ?? 0),
                    Subjects = new List<SubjectMarksDto>()
                };

                var subjectGroups = marksMaster.StudentMarks?.Where(sm => !sm.IsDeleted).GroupBy(sm => sm.SubjectId) ?? Enumerable.Empty<IGrouping<Guid?, StudentMarks>>();

                double totalObtained = 0;
                double totalMax = 0;
                double totalCredits = 0;
                double creditsEarned = 0;

                foreach (var group in subjectGroups)
                {
                    var marksList = group.ToList();
                    var firstSm = marksList.First();
                    var subject = firstSm.Subject;
                    if (subject == null) continue;

                    var subDto = new SubjectMarksDto
                    {
                        SubjectCode = subject.SubjectCode,
                        SubjectName = subject.Name,
                        Credits = double.TryParse(firstSm.CreditMaster?.TotalCredits, out var c) ? c : 0
                    };

                    if (marksList.Count > 0)
                    {
                        var head1 = marksList[0];
                        subDto.Head1Type = head1.Head ?? "TH";
                        subDto.Head1Marks = head1.Marks?.ToString() ?? "AB";
                        subDto.Head1Grace = new string((head1.Grace ?? "").Where(c => !char.IsDigit(c)).ToArray());
                        
                        var credit1 = head1.CreditMaster?.Credits?.FirstOrDefault(cd => cd.Head == head1.Head);
                        subDto.Head1Max = double.TryParse(credit1?.HeadOutOf, out var m1) ? m1 : 100;
                    }

                    if (marksList.Count > 1)
                    {
                        var head2 = marksList[1];
                        subDto.Head2Type = head2.Head ?? "IA";
                        subDto.Head2Marks = head2.Marks?.ToString() ?? "AB";
                        subDto.Head2Grace = new string((head2.Grace ?? "").Where(c => !char.IsDigit(c)).ToArray());
                        
                        var credit2 = head2.CreditMaster?.Credits?.FirstOrDefault(cd => cd.Head == head2.Head);
                        subDto.Head2Max = double.TryParse(credit2?.HeadOutOf, out var m2) ? m2 : 100;
                    }

                    double sumMax = subDto.Head1Max + subDto.Head2Max;
                    int sumObtained = marksList.Sum(m => m.Marks ?? 0);
                    subDto.TotalMax = sumMax;
                    subDto.TotalObtained = sumObtained.ToString();

                    double percentage = sumMax > 0 ? (sumObtained / sumMax) * 100 : 0;
                    var (gp, grade) = GetGradeAndPoint(percentage, gradeMaster);
                    subDto.GradePoint = gp;
                    subDto.Grade = grade;

                    studentDto.Subjects.Add(subDto);

                    totalObtained += sumObtained;
                    totalMax += sumMax;
                    totalCredits += subDto.Credits;
                    if (subDto.GradePoint > 0) creditsEarned += subDto.Credits;
                }

                studentDto.TotalObtained = totalObtained;
                studentDto.TotalMax = totalMax;
                studentDto.TotalCredits = totalCredits;
                studentDto.CreditsEarned = creditsEarned;

                reportDto.Students.Add(studentDto);
            }

            return reportDto;
        }

        public async Task<byte[]> GenerateGazettePdfAsync(GazetteRequestDto request)
        {
            var reportDto = await GetGazetteDataAsync(request);
            var document = new GazetteDocument(reportDto, request);
            return document.GeneratePdf();
        }

        public async Task<byte[]> GenerateGazetteExcelAsync(GazetteRequestDto request)
        {
            var reportDto = await GetGazetteDataAsync(request);

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Gazette");

            // Add Headers
            worksheet.Cells[1, 1].Value = "Seat No";
            worksheet.Cells[1, 2].Value = "Student Name";
            worksheet.Cells[1, 3].Value = "PRN";
            
            int col = 4;
            var firstStudent = reportDto.Students.FirstOrDefault();
            if (firstStudent != null)
            {
                foreach (var sub in firstStudent.Subjects)
                {
                    worksheet.Cells[1, col].Value = $"{sub.SubjectCode} ({sub.Head1Type})";
                    worksheet.Cells[1, col + 1].Value = $"{sub.SubjectCode} ({sub.Head2Type})";
                    worksheet.Cells[1, col + 2].Value = $"{sub.SubjectCode} Total";
                    col += 3;
                }
            }
            
            worksheet.Cells[1, col].Value = "Total Marks";
            worksheet.Cells[1, col + 1].Value = "Credits";
            worksheet.Cells[1, col + 2].Value = "SGPI";
            worksheet.Cells[1, col + 3].Value = "Remark";

            // Add Data
            int row = 2;
            foreach (var student in reportDto.Students)
            {
                worksheet.Cells[row, 1].Value = student.SeatNo;
                worksheet.Cells[row, 2].Value = student.StudentName;
                worksheet.Cells[row, 3].Value = student.PRN;
                
                int dataCol = 4;
                foreach (var sub in student.Subjects)
                {
                    worksheet.Cells[row, dataCol].Value = sub.Head1Marks;
                    worksheet.Cells[row, dataCol + 1].Value = sub.Head2Marks;
                    worksheet.Cells[row, dataCol + 2].Value = sub.TotalObtained;
                    dataCol += 3;
                }
                
                worksheet.Cells[row, dataCol].Value = student.TotalObtained;
                worksheet.Cells[row, dataCol + 1].Value = student.CreditsEarned;
                worksheet.Cells[row, dataCol + 2].Value = student.SGPI;
                worksheet.Cells[row, dataCol + 3].Value = student.Remark;
                row++;
            }

            return await package.GetAsByteArrayAsync();
        }

        public async Task<byte[]> GenerateMarksheetPdfAsync(Guid studId, Guid examId, Guid semId, string pattern, bool includeHistory = false)
        {
            var exam = await _context.Exams.Include(e => e.Course).FirstOrDefaultAsync(e => e.ExamId == examId);

            var ruleSet = await _context.RuleSets
                .Include(rs => rs.GradeMaster)
                    .ThenInclude(gm => gm!.Thresholds)
                .FirstOrDefaultAsync(rs => rs.Pattern!.PatternName == pattern && rs.IsActive && !rs.IsDeleted);
            var gradeMaster = ruleSet?.GradeMaster;

            var marksMaster = await _context.MarksMasters
                .Include(m => m.Student)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.Subject)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.CreditMaster)
                        .ThenInclude(cm => cm!.Credits)
                .FirstOrDefaultAsync(m => m.StdMstId == studId && m.ExamId == examId && !m.IsDeleted);

            if (marksMaster == null)
                throw new Exception("Result not found for the given student and exam. Have you processed the results yet?");

            var student = marksMaster.Student;

            var reportDto = new MarksheetReportDto
            {
                StudentName = student != null ? $"{student.FirstName} {student.LastName}" : "N/A",
                SeatNo = marksMaster.SeatNo ?? "N/A",
                PRN = student?.StudentPRN ?? "N/A",
                StudentId = marksMaster.StudentID ?? "N/A",
                ProgramName = exam?.Course?.Name ?? "N/A",
                ExamName = exam?.Name ?? "Regular Exam",
                Semester = $"Semester {semId}", 
                ResultDate = DateTime.Now,
                SGPI = (double)(marksMaster.SGPI ?? 0),
                CGPI = marksMaster.CGPI.HasValue ? (double)marksMaster.CGPI.Value : null,
                Remark = marksMaster.OverallRemark ?? "Pending",
                Subjects = new List<SubjectMarksDto>()
            };

            var subjectGroups = marksMaster.StudentMarks?.Where(sm => !sm.IsDeleted).GroupBy(sm => sm.SubjectId) ?? Enumerable.Empty<IGrouping<Guid?, StudentMarks>>();

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
                var marksList = group.ToList();
                var firstSm = marksList.First();
                var subject = firstSm.Subject;
                if (subject == null) continue;

                var subDto = new SubjectMarksDto
                {
                    SubjectCode = subject.SubjectCode,
                    SubjectName = subject.Name,
                    Credits = double.TryParse(firstSm.CreditMaster?.TotalCredits, out var c) ? c : 0
                };

                if (marksList.Count > 0)
                {
                    var head1 = marksList[0];
                    subDto.Head1Type = head1.Head ?? "TH";
                    subDto.Head1Marks = head1.Marks?.ToString() ?? "AB";
                    subDto.Head1Grace = new string((head1.Grace ?? "").Where(c => !char.IsDigit(c)).ToArray());
                    
                    var credit1 = head1.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == head1.Head);
                    subDto.Head1Max = double.TryParse(credit1?.HeadOutOf, out var m1) ? m1 : 100;
                }

                if (marksList.Count > 1)
                {
                    var head2 = marksList[1];
                    subDto.Head2Type = head2.Head ?? "IA";
                    subDto.Head2Marks = head2.Marks?.ToString() ?? "AB";
                    subDto.Head2Grace = new string((head2.Grace ?? "").Where(c => !char.IsDigit(c)).ToArray());
                    
                    var credit2 = head2.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == head2.Head);
                    subDto.Head2Max = double.TryParse(credit2?.HeadOutOf, out var m2) ? m2 : 100;
                }

                double sumMax = subDto.Head1Max + subDto.Head2Max;
                int sumObtained = marksList.Sum(m => m.Marks ?? 0);
                subDto.TotalMax = sumMax;
                subDto.TotalObtained = sumObtained.ToString();

                // Grade fallback estimation
                double percentage = sumMax > 0 ? (sumObtained / sumMax) * 100 : 0;
                var (gp, grade) = GetGradeAndPoint(percentage, gradeMaster);
                subDto.GradePoint = gp;
                subDto.Grade = grade;

                reportDto.Subjects.Add(subDto);

                totalObtained += sumObtained;
                totalMax += sumMax;
                totalCredits += subDto.Credits;
                if (subDto.GradePoint > 0) creditsEarned += subDto.Credits;
            }

            reportDto.TotalObtained = totalObtained;
            reportDto.TotalMax = totalMax;
            reportDto.TotalCredits = totalCredits;
            reportDto.CreditsEarned = creditsEarned;

            var document = new MarksheetDocument(reportDto);
            return document.GeneratePdf();
        }

        public async Task<byte[]> GenerateBulkMarksheetPdfAsync(Guid examId, Guid semId, string pattern, string generationType, bool includeHistory = false)
        {
            var exam = await _context.Exams.Include(e => e.Course).FirstOrDefaultAsync(e => e.ExamId == examId);

            var ruleSet = await _context.RuleSets
                .Include(rs => rs.GradeMaster)
                    .ThenInclude(gm => gm!.Thresholds)
                .FirstOrDefaultAsync(rs => rs.Pattern!.PatternName == pattern && rs.IsActive && !rs.IsDeleted);
            var gradeMaster = ruleSet?.GradeMaster;

            var query = _context.MarksMasters
                .Include(m => m.Student)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.Subject)
                .Include(m => m.StudentMarks!)
                    .ThenInclude(sm => sm.CreditMaster)
                        .ThenInclude(cm => cm!.Credits)
                .Where(m => m.ExamId == examId && !m.IsDeleted);

            if (generationType.Equals("pass", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(m => m.OverallRemark == "SUCCESSFUL");
            }
            else if (generationType.Equals("fail", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(m => m.OverallRemark != "SUCCESSFUL");
            }

            var marksMasters = await query.ToListAsync();

            if (!marksMasters.Any())
                throw new Exception("No results found for the given criteria. Have you processed the results yet?");

            var reports = new List<MarksheetReportDto>();

            foreach (var marksMaster in marksMasters)
            {
                var student = marksMaster.Student;
                var reportDto = new MarksheetReportDto
                {
                    StudentName = student != null ? $"{student.FirstName} {student.LastName}" : "N/A",
                    SeatNo = marksMaster.SeatNo ?? "N/A",
                    PRN = student?.StudentPRN ?? "N/A",
                    StudentId = marksMaster.StudentID ?? "N/A",
                    ProgramName = exam?.Course?.Name ?? "N/A",
                    ExamName = exam?.Name ?? "Regular Exam",
                    Semester = $"Semester {semId}",
                    ResultDate = DateTime.Now,
                    SGPI = (double)(marksMaster.SGPI ?? 0),
                    CGPI = marksMaster.CGPI.HasValue ? (double)marksMaster.CGPI.Value : null,
                    Remark = marksMaster.OverallRemark ?? "Pending",
                    Subjects = new List<SubjectMarksDto>()
                };

                var subjectGroups = marksMaster.StudentMarks?.Where(sm => !sm.IsDeleted).GroupBy(sm => sm.SubjectId) ?? Enumerable.Empty<IGrouping<Guid?, StudentMarks>>();

                if (includeHistory && student?.StdMstId != null)
                {
                    reportDto.PastSemesters = await GetSemesterHistoryAsync(student.StdMstId);
                }

                double totalObtained = 0;
                double totalMax = 0;
                double totalCredits = 0;
                double creditsEarned = 0;

                foreach (var group in subjectGroups)
                {
                    var marksList = group.ToList();
                    var firstSm = marksList.First();
                    var subject = firstSm.Subject;
                    if (subject == null) continue;

                    var subDto = new SubjectMarksDto
                    {
                        SubjectCode = subject.SubjectCode,
                        SubjectName = subject.Name,
                        Credits = double.TryParse(firstSm.CreditMaster?.TotalCredits, out var c) ? c : 0
                    };

                    if (marksList.Count > 0)
                    {
                        var head1 = marksList[0];
                        subDto.Head1Type = head1.Head ?? "TH";
                        subDto.Head1Marks = head1.Marks?.ToString() ?? "AB";
                        subDto.Head1Grace = new string((head1.Grace ?? "").Where(c => !char.IsDigit(c)).ToArray());
                        
                        var credit1 = head1.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == head1.Head);
                        subDto.Head1Max = double.TryParse(credit1?.HeadOutOf, out var m1) ? m1 : 100;
                    }

                    if (marksList.Count > 1)
                    {
                        var head2 = marksList[1];
                        subDto.Head2Type = head2.Head ?? "IA";
                        subDto.Head2Marks = head2.Marks?.ToString() ?? "AB";
                        subDto.Head2Grace = new string((head2.Grace ?? "").Where(c => !char.IsDigit(c)).ToArray());
                        
                        var credit2 = head2.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == head2.Head);
                        subDto.Head2Max = double.TryParse(credit2?.HeadOutOf, out var m2) ? m2 : 100;
                    }

                    double sumMax = subDto.Head1Max + subDto.Head2Max;
                    int sumObtained = marksList.Sum(m => m.Marks ?? 0);
                    subDto.TotalMax = sumMax;
                    subDto.TotalObtained = sumObtained.ToString();

                    double percentage = sumMax > 0 ? (sumObtained / sumMax) * 100 : 0;
                    var (gp, grade) = GetGradeAndPoint(percentage, gradeMaster);
                    subDto.GradePoint = gp;
                    subDto.Grade = grade;

                    reportDto.Subjects.Add(subDto);

                    totalObtained += sumObtained;
                    totalMax += sumMax;
                    totalCredits += subDto.Credits;
                    if (subDto.GradePoint > 0) creditsEarned += subDto.Credits;
                }

                reportDto.TotalObtained = totalObtained;
                reportDto.TotalMax = totalMax;
                reportDto.TotalCredits = totalCredits;
                reportDto.CreditsEarned = creditsEarned;

                reports.Add(reportDto);
            }

            var document = new BulkMarksheetDocument(reports);
            return document.GeneratePdf();
        }

        private (int GradePoint, string Grade) GetGradeAndPoint(double percentage, GradeMaster? gradeMaster)
        {
            if (gradeMaster?.Thresholds != null && gradeMaster.Thresholds.Any())
            {
                var threshold = gradeMaster.Thresholds
                    .OrderByDescending(t => t.MinPercentage)
                    .FirstOrDefault(t => (decimal)percentage >= t.MinPercentage && (decimal)percentage <= t.MaxPercentage);
                
                if (threshold != null) return ((int)threshold.GradePoint, threshold.Grade ?? "P");
            }

            // Fallback
            if (percentage >= 80) return (10, "O");
            if (percentage >= 75) return (9, "A");
            if (percentage >= 70) return (8, "B");
            if (percentage >= 60) return (7, "C");
            if (percentage >= 50) return (6, "D");
            if (percentage >= 45) return (5, "E");
            if (percentage >= 40) return (4, "P");
            return (0, "F");
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
    }
}
