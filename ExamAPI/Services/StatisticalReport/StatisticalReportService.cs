using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace ExamAPI.Services.StatisticalReport;

/// <summary>
/// Subject-wise statistics for a single processed exam. This service deliberately consumes
/// persisted result-engine output rather than re-evaluating raw marks: combined/head-wise
/// passing, absences and ordinance grace therefore mean exactly what Result processing decided.
/// </summary>
public sealed class StatisticalReportService : IStatisticalReportService
{
    private readonly ApplicationDbContext _context;

    public StatisticalReportService(ApplicationDbContext context) => _context = context;

    public async Task<ApiResponseDto<StatisticalReportDto>> GetReportAsync(StatisticalReportRequestDto request, Guid collegeId)
    {
        if (request.CourseId == Guid.Empty || request.AcademicYearId == Guid.Empty || request.ExamId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.SemesterId) || string.IsNullOrWhiteSpace(request.Pattern))
        {
            return Failure("Course, academic year, semester, pattern and exam are required.");
        }

        var exam = await _context.Exams
            .AsNoTracking()
            .Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.ExamId == request.ExamId
                && e.CourseId == request.CourseId
                && e.AcademicYearAYID == request.AcademicYearId
                && e.Course != null
                && e.Course.CollegeId == collegeId
                && !e.IsDeleted);

        if (exam == null)
        {
            return Failure("The selected exam is unavailable for the selected course and academic year.");
        }

        var examIds = new List<Guid> { request.ExamId };
        var mergedExamName = string.Empty;
        if (request.MergeExam)
        {
            if (!request.MergedExamId.HasValue || request.MergedExamId.Value == request.ExamId)
            {
                return Failure("Select a different exam to merge.");
            }

            var mergedExam = await _context.Exams.AsNoTracking()
                .FirstOrDefaultAsync(e => e.ExamId == request.MergedExamId.Value
                    && e.CourseId == request.CourseId
                    && e.AcademicYearAYID == request.AcademicYearId
                    && !e.IsDeleted);
            if (mergedExam == null)
            {
                return Failure("The exam selected for merging is unavailable for the selected course and academic year.");
            }

            examIds.Add(mergedExam.ExamId);
            mergedExamName = mergedExam.Name ?? "Merged exam";
        }

        var marksMasters = await _context.MarksMasters
            .AsNoTracking()
            .Include(m => m.SubjectResults)
                .ThenInclude(r => r.Subject)
            .Where(m => !m.IsDeleted
                && m.ExamId.HasValue
                && examIds.Contains(m.ExamId.Value)
                && m.AcademicYearAYID == request.AcademicYearId
                && m.SemesterId == request.SemesterId
                && m.Pattern == request.Pattern
                && m.CollegeId == collegeId)
            .ToListAsync();

        if (marksMasters.Count == 0)
        {
            return Failure("No students are assigned to the selected exam, semester and pattern.");
        }

        if (marksMasters.Any(m => m.SubjectResults == null || m.SubjectResults.Count == 0))
        {
            return Failure("Results have not been processed for every assigned student. Generate results before exporting the statistical report.");
        }

        var college = await _context.Colleges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CollegeId == collegeId && !c.IsDeleted);
        var academicYear = await _context.AcademicYears.AsNoTracking()
            .FirstOrDefaultAsync(year => year.AYID == request.AcademicYearId
                && year.CollegeId == collegeId
                && !year.IsDeleted);

        var subjectResults = marksMasters
            .SelectMany(m => m.SubjectResults!
                .Where(result => !result.IsDeleted)
                .Select(result => new { MarksMaster = m, Result = result }))
            .ToList();

        // A legacy merge took the best pivoted mark per student/subject. The modern equivalent
        // is explicit and respects result semantics: a passed processed attempt always wins;
        // otherwise the highest processed percentage wins. This works for both combined and
        // head-wise subjects because IsPassed was decided by the shared result engine.
        var reportingResults = request.MergeExam
            ? subjectResults
                .GroupBy(item => new
                {
                    Student = item.MarksMaster.StdMstId?.ToString()
                        ?? item.MarksMaster.StudentID
                        ?? item.MarksMaster.MarksId.ToString(),
                    item.Result.SubjectId
                })
                .Select(group => group
                    .OrderByDescending(item => item.Result.IsPassed)
                    .ThenByDescending(item => Percentage(item.Result))
                    .ThenByDescending(item => item.Result.ObtainedTotal)
                    .First())
                .ToList()
            : subjectResults;

        var rows = reportingResults
            .GroupBy(item => item.Result.SubjectId)
            .OrderBy(group => group.First().Result.Subject?.SubjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) =>
            {
                var passed = group.Where(item => item.Result.IsPassed).ToList();
                var between40And60 = passed.Count(item => Percentage(item.Result) >= 40m && Percentage(item.Result) < 60m);
                var atOrAbove60 = passed.Count(item => Percentage(item.Result) >= 60m);
                var subject = group.First().Result.Subject;

                return new StatisticalReportRowDto
                {
                    SrNo = index + 1,
                    SubjectCode = subject?.SubjectCode ?? "",
                    SubjectName = subject?.Name ?? "Unnamed subject",
                    TotalAppeared = group.Select(item => item.MarksMaster.MarksId).Distinct().Count(),
                    TotalPassed = passed.Select(item => item.MarksMaster.MarksId).Distinct().Count(),
                    PassingPercentage = Percentage(part: passed.Select(item => item.MarksMaster.MarksId).Distinct().Count(), whole: group.Select(item => item.MarksMaster.MarksId).Distinct().Count()),
                    PassedBetween40And60 = between40And60,
                    PassedAtOrAbove60 = atOrAbove60,
                    GraceMarksAwarded = group.Sum(item => item.Result.GraceApplied)
                };
            })
            .ToList();

        var overallResults = request.MergeExam
            ? marksMasters
                .GroupBy(m => m.StdMstId?.ToString() ?? m.StudentID ?? m.MarksId.ToString())
                .Select(group => group.OrderByDescending(m => OverallRemarks.IsPass(m.OverallRemark)).First())
                .ToList()
            : marksMasters;
        var totalAppeared = overallResults.Count;
        var totalPassed = overallResults.Count(m => OverallRemarks.IsPass(m.OverallRemark));
        var report = new StatisticalReportDto
        {
            CollegeName = college?.Name ?? "College Name Not Found",
            CollegeAddress = college?.Address,
            CourseName = exam.Course?.Name ?? "Course",
            AcademicYearName = academicYear?.FullDuration ?? academicYear?.ShortDuration ?? "",
            SemesterName = FormatSemester(request.SemesterId),
            Pattern = request.Pattern,
            ExamName = request.MergeExam ? $"{exam.Name} + {mergedExamName}" : exam.Name ?? "Exam",
            GeneratedAt = DateTime.Now,
            Rows = rows,
            TotalStudentsAppeared = totalAppeared,
            TotalStudentsPassed = totalPassed,
            OverallPassingPercentage = Percentage(totalPassed, totalAppeared)
        };

        return new ApiResponseDto<StatisticalReportDto>
        {
            Success = true,
            Message = $"{rows.Count} subject statistic(s) loaded.",
            Data = report
        };
    }

    public async Task<ApiResponseDto<byte[]>> GenerateExcelAsync(StatisticalReportRequestDto request, Guid collegeId)
    {
        var reportResponse = await GetReportAsync(request, collegeId);
        if (!reportResponse.Success || reportResponse.Data == null)
        {
            return new ApiResponseDto<byte[]> { Success = false, Message = reportResponse.Message };
        }

        var report = reportResponse.Data;
        ExcelPackage.License.SetNonCommercialPersonal("ReactApi Project");
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Statistical Report");
        worksheet.View.ShowGridLines = false;
        worksheet.PrinterSettings.Orientation = eOrientation.Landscape;
        worksheet.PrinterSettings.PaperSize = ePaperSize.A4;
        worksheet.PrinterSettings.FitToPage = true;
        worksheet.PrinterSettings.FitToWidth = 1;
        worksheet.PrinterSettings.FitToHeight = 0;
        worksheet.PrinterSettings.LeftMargin = 0.25;
        worksheet.PrinterSettings.RightMargin = 0.25;

        const int totalColumns = 9;
        MergeAndStyle(worksheet, 1, totalColumns, report.CollegeName, 18, true);
        if (!string.IsNullOrWhiteSpace(report.CollegeAddress))
        {
            MergeAndStyle(worksheet, 2, totalColumns, report.CollegeAddress, 10, false);
        }
        MergeAndStyle(worksheet, 3, totalColumns, $"STATISTICAL REPORT — {report.CourseName} | {report.SemesterName} | {report.Pattern}", 12, true);
        MergeAndStyle(worksheet, 4, totalColumns, $"EXAMINATION: {report.ExamName}    ACADEMIC YEAR: {report.AcademicYearName}", 11, true);
        MergeAndStyle(worksheet, 5, totalColumns, "Subject-wise result statistics generated from processed examination results", 9, false);
        worksheet.Row(5).Height = 22;

        var headers = new[]
        {
            "SR. NO.", "SUBJECT", "SUBJECT CODE", "TOTAL STUDENTS APPEARED", "TOTAL STUDENTS PASSED",
            "PASSING PERCENTAGE", "PASSED: 40% TO <60%", "PASSED: 60% AND ABOVE", "GRACE MARKS AWARDED"
        };
        for (var column = 1; column <= headers.Length; column++)
        {
            worksheet.Cells[7, column].Value = headers[column - 1];
        }

        using (var header = worksheet.Cells[7, 1, 7, totalColumns])
        {
            header.Style.Font.Bold = true;
            header.Style.Font.Size = 9;
            header.Style.WrapText = true;
            header.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            header.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            header.Style.Fill.PatternType = ExcelFillStyle.Solid;
            header.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(30, 64, 175));
            header.Style.Font.Color.SetColor(System.Drawing.Color.White);
            ApplyBorders(header);
        }
        worksheet.Row(7).Height = 42;

        var row = 8;
        foreach (var item in report.Rows)
        {
            worksheet.Cells[row, 1].Value = item.SrNo;
            worksheet.Cells[row, 2].Value = item.SubjectName;
            worksheet.Cells[row, 3].Value = item.SubjectCode;
            worksheet.Cells[row, 4].Value = item.TotalAppeared;
            worksheet.Cells[row, 5].Value = item.TotalPassed;
            worksheet.Cells[row, 6].Value = item.PassingPercentage / 100m;
            worksheet.Cells[row, 6].Style.Numberformat.Format = "0.00%";
            worksheet.Cells[row, 7].Value = item.PassedBetween40And60;
            worksheet.Cells[row, 8].Value = item.PassedAtOrAbove60;
            worksheet.Cells[row, 9].Value = item.GraceMarksAwarded;

            using var dataRange = worksheet.Cells[row, 1, row, totalColumns];
            dataRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            dataRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            ApplyBorders(dataRange);
            if (row % 2 == 0)
            {
                dataRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                dataRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(239, 246, 255));
            }
            worksheet.Cells[row, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            row++;
        }

        row++;
        WriteSummary(worksheet, row++, totalColumns, "TOTAL NO. OF STUDENTS APPEARED", report.TotalStudentsAppeared.ToString());
        WriteSummary(worksheet, row++, totalColumns, "TOTAL NO. OF STUDENTS PASSED", report.TotalStudentsPassed.ToString());
        WriteSummary(worksheet, row++, totalColumns, "OVERALL PASSING PERCENTAGE", $"{report.OverallPassingPercentage:0.00}%");
        WriteSummary(worksheet, row++, totalColumns, "REPORT GENERATED", report.GeneratedAt.ToString("dd MMM yyyy, hh:mm tt"));

        worksheet.Column(1).Width = 10;
        worksheet.Column(2).Width = 36;
        worksheet.Column(3).Width = 16;
        worksheet.Column(4).Width = 18;
        worksheet.Column(5).Width = 18;
        worksheet.Column(6).Width = 17;
        worksheet.Column(7).Width = 18;
        worksheet.Column(8).Width = 19;
        worksheet.Column(9).Width = 18;
        worksheet.Cells[1, 1, row, totalColumns].Style.WrapText = true;
        worksheet.View.FreezePanes(8, 1);

        return new ApiResponseDto<byte[]>
        {
            Success = true,
            Message = "Statistical report exported successfully.",
            Data = package.GetAsByteArray()
        };
    }

    private static ApiResponseDto<StatisticalReportDto> Failure(string message) => new() { Success = false, Message = message };

    private static decimal Percentage(StudentSubjectResult result) => Percentage(result.ObtainedTotal, result.OutOfTotal);

    private static decimal Percentage(int part, int whole) => whole <= 0 ? 0m : Math.Round(part * 100m / whole, 2);

    private static string FormatSemester(string semesterId)
    {
        var number = new string(semesterId.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(number) ? semesterId : $"Semester {number}";
    }

    private static void MergeAndStyle(ExcelWorksheet worksheet, int row, int totalColumns, string? value, int size, bool bold)
    {
        var range = worksheet.Cells[row, 1, row, totalColumns];
        range.Merge = true;
        range.Value = value;
        range.Style.Font.Size = size;
        range.Style.Font.Bold = bold;
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
    }

    private static void WriteSummary(ExcelWorksheet worksheet, int row, int totalColumns, string label, string value)
    {
        var range = worksheet.Cells[row, 1, row, totalColumns];
        range.Merge = true;
        range.Value = $"{label}: {value}";
        range.Style.Font.Bold = true;
        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(219, 234, 254));
        ApplyBorders(range);
    }

    private static void ApplyBorders(ExcelRange range)
    {
        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
    }
}
