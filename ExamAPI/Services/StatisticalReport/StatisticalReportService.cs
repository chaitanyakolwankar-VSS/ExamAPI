using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;

namespace ExamAPI.Services.StatisticalReport;

/// <summary>
/// Subject-wise statistics for a single processed exam. This service deliberately consumes
/// persisted result-engine output rather than re-evaluating raw marks: combined/head-wise
/// passing, absences and ordinance grace therefore mean exactly what Result processing decided.
/// </summary>
public sealed class StatisticalReportService : IStatisticalReportService
{
    private readonly ApplicationDbContext _context;

    private readonly ExamAPI.Services.Files.IFileStorage? _storage;

    public StatisticalReportService(ApplicationDbContext context, ExamAPI.Services.Files.IFileStorage? storage = null)
    {
        _context = context;
        _storage = storage;
    }

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
                    // GraceApplied stores combined-subject ordinance grace only. The processed
                    // uplift also includes head-wise ordinance grace and resolution marks.
                    // Use the verdict's snapshot, without adding combined grace twice.
                    GraceMarksAwarded = group.Sum(item => Math.Max(0,
                        item.Result.ObtainedTotal - item.Result.RawObtainedTotal))
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
            CollegeName = ExamAPI.Services.Report.CollegeBranding.DisplayName(college?.Name, college?.CollegeCode),
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
        using var workbook = Report.ExcelStyles.NewWorkbook();
        var worksheet = workbook.Worksheets.Add("Statistical Report");
        worksheet.ShowGridLines = false;
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        worksheet.PageSetup.FitToPages(1, 0);
        worksheet.PageSetup.Margins.Left = 0.25;
        worksheet.PageSetup.Margins.Right = 0.25;
        worksheet.PageSetup.Margins.Top = 0.748;
        worksheet.PageSetup.Margins.Bottom = 0.748;
        worksheet.PageSetup.Margins.Header = 0.315;
        worksheet.PageSetup.Margins.Footer = 0.315;

        const int totalColumns = 9;
        MergeAndStyle(worksheet, 1, totalColumns, report.CollegeName, 18, true);
        if (!string.IsNullOrWhiteSpace(report.CollegeAddress))
        {
            MergeAndStyle(worksheet, 2, totalColumns, report.CollegeAddress, 10, false);
        }
        // College Details branding: the banner (added below, once the column widths are known) or the
        // logo at the top left of the name. Pictures sit above the grid, so no data cell moves.
        var branding = await Report.CollegeBranding.LoadAsync(_context, _storage, collegeId);
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
            worksheet.Cell(7, column).Value = headers[column - 1];
        }

        var header = worksheet.Range(7, 1, 7, totalColumns).Style;
        header.Font.Bold = true;
        header.Font.FontSize = 9;
        header.Alignment.WrapText = true;
        header.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Fill.BackgroundColor = XLColor.FromArgb(30, 64, 175);
        header.Font.FontColor = XLColor.White;
        Report.ExcelStyles.ThinBorders(header);
        worksheet.Row(7).Height = 42;

        var row = 8;
        foreach (var item in report.Rows)
        {
            worksheet.Cell(row, 1).Value = item.SrNo;
            worksheet.Cell(row, 2).Value = item.SubjectName;
            worksheet.Cell(row, 3).Value = item.SubjectCode;
            worksheet.Cell(row, 4).Value = item.TotalAppeared;
            worksheet.Cell(row, 5).Value = item.TotalPassed;
            worksheet.Cell(row, 6).Value = item.PassingPercentage / 100m;
            worksheet.Cell(row, 6).Style.NumberFormat.NumberFormatId = 10; // built-in 0.00%
            worksheet.Cell(row, 7).Value = item.PassedBetween40And60;
            worksheet.Cell(row, 8).Value = item.PassedAtOrAbove60;
            worksheet.Cell(row, 9).Value = item.GraceMarksAwarded;

            var data = worksheet.Range(row, 1, row, totalColumns).Style;
            data.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            data.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            Report.ExcelStyles.ThinBorders(data);
            if (row % 2 == 0)
            {
                data.Fill.BackgroundColor = XLColor.FromArgb(239, 246, 255);
            }
            worksheet.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            row++;
        }

        row++;
        WriteSummary(worksheet, row++, totalColumns, "TOTAL NO. OF STUDENTS APPEARED", report.TotalStudentsAppeared.ToString());
        WriteSummary(worksheet, row++, totalColumns, "TOTAL NO. OF STUDENTS PASSED", report.TotalStudentsPassed.ToString());
        WriteSummary(worksheet, row++, totalColumns, "OVERALL PASSING PERCENTAGE", $"{report.OverallPassingPercentage:0.00}%");
        WriteSummary(worksheet, row++, totalColumns, "REPORT GENERATED", report.GeneratedAt.ToString("dd MMM yyyy, hh:mm tt"));

        Report.ExcelStyles.SetWidth(worksheet.Column(1), 10);
        Report.ExcelStyles.SetWidth(worksheet.Column(2), 36);
        Report.ExcelStyles.SetWidth(worksheet.Column(3), 16);
        Report.ExcelStyles.SetWidth(worksheet.Column(4), 18);
        Report.ExcelStyles.SetWidth(worksheet.Column(5), 18);
        Report.ExcelStyles.SetWidth(worksheet.Column(6), 17);
        Report.ExcelStyles.SetWidth(worksheet.Column(7), 18);
        Report.ExcelStyles.SetWidth(worksheet.Column(8), 19);
        Report.ExcelStyles.SetWidth(worksheet.Column(9), 18);
        worksheet.Range(1, 1, row, totalColumns).Style.Alignment.WrapText = true;
        worksheet.SheetView.FreezeRows(7);

        // Banner -> logo + name -> name only. The banner covers rows 1-2 (name + address rows), so
        // those texts are not printed again and every row below keeps its position.
        if (Report.ExcelBranding.TryAddBanner(worksheet, branding.Banner, 1, totalColumns, 2) > 0)
        {
            worksheet.Cell(1, 1).Value = Blank.Value;
            worksheet.Cell(2, 1).Value = Blank.Value;
        }
        else
        {
            // Row 1 (18pt college name) gets an explicit height rather than relying on autofit.
            worksheet.Row(1).Height = 26;
            if (Report.ExcelBranding.TryAddLogo(worksheet, branding.Logo, 1, 1, 96, 44))
                worksheet.Row(1).Height = 32;
        }

        return new ApiResponseDto<byte[]>
        {
            Success = true,
            Message = "Statistical report exported successfully.",
            Data = Report.ExcelStyles.ToBytes(workbook)
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

    private static void MergeAndStyle(IXLWorksheet worksheet, int row, int totalColumns, string? value, int size, bool bold)
    {
        var range = worksheet.Range(row, 1, row, totalColumns);
        range.Merge();
        range.FirstCell().Value = value;
        range.Style.Font.FontSize = size;
        range.Style.Font.Bold = bold;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void WriteSummary(IXLWorksheet worksheet, int row, int totalColumns, string label, string value)
    {
        var range = worksheet.Range(row, 1, row, totalColumns);
        range.Merge();
        range.FirstCell().Value = $"{label}: {value}";
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromArgb(219, 234, 254);
        Report.ExcelStyles.ThinBorders(range.Style);
    }
}
