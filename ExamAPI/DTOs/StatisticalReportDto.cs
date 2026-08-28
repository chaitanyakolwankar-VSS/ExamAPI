namespace ExamAPI.DTOs;

/// <summary>Filters one processed exam's subject-wise statistical report.</summary>
public sealed class StatisticalReportRequestDto
{
    public Guid CourseId { get; set; }
    public Guid AcademicYearId { get; set; }
    public Guid ExamId { get; set; }
    public bool MergeExam { get; set; }
    public Guid? MergedExamId { get; set; }
    public string SemesterId { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
}

/// <summary>The complete data set behind the on-screen preview and Excel export.</summary>
public sealed class StatisticalReportDto
{
    public string CollegeName { get; set; } = string.Empty;
    public string? CollegeAddress { get; set; }
    public string CourseName { get; set; } = string.Empty;
    public string AcademicYearName { get; set; } = string.Empty;
    public string SemesterName { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public string ExamName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public List<StatisticalReportRowDto> Rows { get; set; } = new();
    public int TotalStudentsAppeared { get; set; }
    public int TotalStudentsPassed { get; set; }
    public decimal OverallPassingPercentage { get; set; }
}

/// <summary>One subject's statistics. Percentages are based on the processed subject result.</summary>
public sealed class StatisticalReportRowDto
{
    public int SrNo { get; set; }
    public string SubjectCode { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public int TotalAppeared { get; set; }
    public int TotalPassed { get; set; }
    public decimal PassingPercentage { get; set; }
    public int PassedBetween40And60 { get; set; }
    public int PassedAtOrAbove60 { get; set; }
    public int GraceMarksAwarded { get; set; }
}
