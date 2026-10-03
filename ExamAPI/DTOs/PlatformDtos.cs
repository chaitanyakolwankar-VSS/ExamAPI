namespace ExamAPI.DTOs
{
    // DTOs for the platform (developer) console: onboarding colleges and their admins.
    // Reference types are nullable on purpose: validation is done in the service so the caller
    // gets one clear 400 message instead of a ModelState dictionary.

    public class ProvisionCollegeRequest
    {
        public string? Name { get; set; }
        public string? CollegeCode { get; set; }
        public string? CollegeCenter { get; set; }
        public string? Address { get; set; }
        public string? ContactEmail { get; set; }
        public string? ContactPhone { get; set; }

        public IFormFile? Logo { get; set; }
        public IFormFile? Banner { get; set; }

        public AcademicYearInput? AcademicYear { get; set; }

        /// <summary>At least one branch (course): name + code.</summary>
        public List<BranchInput>? Branches { get; set; }

        /// <summary>At least one pattern name, e.g. "NEP".</summary>
        public List<string>? Patterns { get; set; }

        /// <summary>
        /// College whose grade scale(s) and ordinance rule set(s) are cloned. Optional, but a
        /// college without them cannot process results.
        /// </summary>
        public Guid? TemplateCollegeId { get; set; }

        /// <summary>0 to 2 admins.</summary>
        public List<AdminInput>? Admins { get; set; }
    }

    public class AcademicYearInput
    {
        public string? FullDuration { get; set; }
        public string? ShortDuration { get; set; }
        public bool IsCurrent { get; set; } = true;
    }

    public class BranchInput
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
    }

    public class AdminInput
    {
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Password { get; set; }
    }

    public class ProvisionItemCount
    {
        public string Item { get; set; } = string.Empty;
        public int Created { get; set; }
        public int AlreadyPresent { get; set; }
    }

    public class ProvisionAdminResult
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        /// <summary>"created" or "already present".</summary>
        public string Status { get; set; } = string.Empty;
    }

    public class ProvisionSummary
    {
        public Guid CollegeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CollegeCode { get; set; } = string.Empty;
        /// <summary>True when the college code was already registered (re-run: only missing parts were filled).</summary>
        public bool AlreadyExisted { get; set; }
        public Guid? TemplateCollegeId { get; set; }
        public List<ProvisionItemCount> Items { get; set; } = new();
        public List<ProvisionAdminResult> Admins { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    // ---------- list / detail ----------

    public class PlatformCollegeListItem
    {
        public Guid CollegeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CollegeCode { get; set; } = string.Empty;
        public int AdminCount { get; set; }
        public int BranchCount { get; set; }
        public string? CurrentAcademicYear { get; set; }
        public bool HasLogo { get; set; }
        public bool HasBanner { get; set; }
    }

    public class PlatformCollegeDetail
    {
        public Guid CollegeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CollegeCode { get; set; } = string.Empty;
        public string CollegeCenter { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public bool HasLogo { get; set; }
        public bool HasBanner { get; set; }
        public List<PlatformBranchDto> Branches { get; set; } = new();
        public List<PlatformPatternDto> Patterns { get; set; } = new();
        public List<PlatformAcademicYearDto> AcademicYears { get; set; } = new();
        public List<PlatformAdminDto> Admins { get; set; } = new();
        public int GradeScaleCount { get; set; }
        public int RuleSetCount { get; set; }
    }

    public class PlatformBranchDto
    {
        public Guid CourseId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CourseCode { get; set; } = string.Empty;
    }

    public class PlatformPatternDto
    {
        public Guid PatternId { get; set; }
        public string PatternName { get; set; } = string.Empty;
    }

    public class PlatformAcademicYearDto
    {
        public Guid AYID { get; set; }
        public string FullDuration { get; set; } = string.Empty;
        public string? ShortDuration { get; set; }
        public bool IsCurrent { get; set; }
    }

    public class PlatformAdminDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    // ---------- single-item adds ----------

    public class AddAcademicYearRequest
    {
        public string? FullDuration { get; set; }
        public string? ShortDuration { get; set; }
        /// <summary>Make this the current year (the previous current year is unset).</summary>
        public bool SetCurrent { get; set; }
    }
}
