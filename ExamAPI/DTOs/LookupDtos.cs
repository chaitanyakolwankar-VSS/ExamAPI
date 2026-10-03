namespace ExamAPI.DTOs
{
    // Lookup data shared by many screens (T-19 layer A). Serialized camelCase.

    public class LookupAcademicYearDto
    {
        public Guid Ayid { get; set; }
        public string FullDuration { get; set; } = "";
        public string ShortDuration { get; set; } = "";
        public bool IsCurrent { get; set; }
    }

    public class LookupCourseDto
    {
        public Guid CourseId { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
    }

    public class LookupPatternDto
    {
        public Guid PatternId { get; set; }
        public string Name { get; set; } = "";
    }

    public class LookupSemesterDto
    {
        /// <summary>The stored id, e.g. "Sem-6".</summary>
        public string Value { get; set; } = "";
        /// <summary>Display text, e.g. "Semester VI".</summary>
        public string Label { get; set; } = "";
        public int Number { get; set; }
    }

    public class LookupGradeMasterDto
    {
        public Guid GradeMasterId { get; set; }
        public string Name { get; set; } = "";
    }

    public class LookupCollegeDto
    {
        public Guid CollegeId { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public string Centre { get; set; } = "";
        public bool HasLogo { get; set; }
        public bool HasBanner { get; set; }
    }

    public class LookupBootstrapDto
    {
        public List<LookupAcademicYearDto> AcademicYears { get; set; } = new();
        public List<LookupCourseDto> Courses { get; set; } = new();
        public List<LookupPatternDto> Patterns { get; set; } = new();
        public List<LookupSemesterDto> Semesters { get; set; } = new();
        public List<LookupGradeMasterDto> GradeMasters { get; set; } = new();
        public LookupCollegeDto? College { get; set; }
    }

    public class LookupExamDto
    {
        public Guid ExamId { get; set; }
        public string Name { get; set; } = "";
        /// <summary>Name, plus " (Revaluation)" for a revaluation exam.</summary>
        public string DisplayName { get; set; } = "";
        public string? ExamType { get; set; }
        public bool IsActive { get; set; }
        public bool IsRevaluation { get; set; }
        public Guid? RevaluationForExamId { get; set; }
        public bool IsLocked { get; set; }
    }

    public class LookupSubjectDto
    {
        public Guid SubjectId { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
    }
}
