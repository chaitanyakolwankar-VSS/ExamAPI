namespace ExamAPI.DTOs
{
    /// <summary>
    /// The ordinance rule set governing this screen, echoed to the client so the operator can
    /// see what is being applied and why a cell is locked. There is no separate assignment
    /// config table -- this is resolved from RuleSet / Rule / RuleAction.
    /// </summary>
    public class AtktPolicyDto
    {
        public Guid RuleSetId { get; set; }
        public string RuleSetName { get; set; } = string.Empty;
        public string? ExamType { get; set; }

        /// <summary>One of <see cref="Models.AssignmentModes"/>.</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>
        /// False when no rule set carries an AllowExamAssignment action for this exam type and
        /// the built-in fallback is in force.
        /// </summary>
        public bool IsConfigured { get; set; }

        /// <summary>Subject statuses in scope. Empty means every subject.</summary>
        public List<string> SubjectScopes { get; set; } = new();

        /// <summary>Heads that get re-attempted. Empty means every head of a selected subject.</summary>
        public List<string> HeadTypes { get; set; } = new();

        /// <summary>From RuleAction.MaxTargetCount. Null means no cap.</summary>
        public int? MaxSubjectsPerStudent { get; set; }

        /// <summary>Names of the rules that grant assignment, for display.</summary>
        public List<string> Rules { get; set; } = new();
    }

    public class AtktExamOptionDto
    {
        public Guid ExamId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public string? ExamType { get; set; }
        public bool IsRevaluation { get; set; }
        public Guid? RevaluationForExamId { get; set; }
        public bool IsLocked { get; set; }
    }

    /// <summary>One head of one subject column, carrying its own thresholds.</summary>
    public class AtktHeadDto
    {
        public string Head { get; set; } = string.Empty;      // positional key, "H1"
        public string HeadType { get; set; } = string.Empty;  // semantic label, "ESE"
        public int OutOf { get; set; }
        public int Pass { get; set; }
    }

    /// <summary>
    /// A subject column of the matrix. The client renders columns from this list -- nothing
    /// about the subject grid is hard-coded on the front end.
    /// </summary>
    public class AtktSubjectColumnDto
    {
        public Guid SubjectId { get; set; }
        public Guid CreditsId { get; set; }
        public string SubjectCode { get; set; } = string.Empty;
        public string SubjectName { get; set; } = string.Empty;
        public string PassingStrategy { get; set; } = string.Empty;
        public int OutOfTotal { get; set; }
        public int RequiredToPass { get; set; }
        public int Order { get; set; }
        public List<AtktHeadDto> Heads { get; set; } = new();
    }

    /// <summary>Per-subject state for one student.</summary>
    public class AtktCellDto
    {
        public Guid SubjectId { get; set; }
        public Guid CreditsId { get; set; }

        /// <summary>One of <see cref="Models.SubjectStatuses"/>, or "NotAttempted".</summary>
        public string Status { get; set; } = string.Empty;

        public int ObtainedTotal { get; set; }
        public int OutOfTotal { get; set; }
        public int RequiredToPass { get; set; }

        /// <summary>Marks short of passing, from SubjectPassEvaluator. 0 when cleared.</summary>
        public int Deficit { get; set; }

        public bool IsAbsent { get; set; }

        /// <summary>Whether the operator may tick this cell, per the policy.</summary>
        public bool Selectable { get; set; }

        /// <summary>Current selection -- true when the student is appearing for this subject.</summary>
        public bool Selected { get; set; }

        /// <summary>Human-readable explanation when <see cref="Selectable"/> is false.</summary>
        public string? Reason { get; set; }
    }

    public class AtktStudentRowDto
    {
        public Guid StdMstId { get; set; }
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string? SeatNo { get; set; }

        public Guid? SourceMarksId { get; set; }
        public Guid? SourceExamId { get; set; }
        public string? SourceExamName { get; set; }

        /// <summary>Non-null once the student has been assigned to the target exam.</summary>
        public Guid? TargetMarksId { get; set; }
        public bool IsAssigned { get; set; }

        /// <summary>Legacy "No. of ATKT" -- subjects not cleared in the source attempt.</summary>
        public int BacklogCount { get; set; }

        public bool CanDelete { get; set; }
        public string? DeleteBlockedReason { get; set; }

        public List<AtktCellDto> Cells { get; set; } = new();
    }

    /// <summary>Filter block shared by every read and write on this screen.</summary>
    public class AtktMatrixRequest
    {
        public Guid CourseId { get; set; }
        public Guid Ayid { get; set; }
        public string Semester { get; set; } = string.Empty;
        public string Pattern { get; set; } = string.Empty;

        /// <summary>One of <see cref="Models.AssignmentModes"/>.</summary>
        public string Mode { get; set; } = Models.AssignmentModes.Atkt;

        /// <summary>Required for revaluation; for ATKT the student's latest attempt is used.</summary>
        public Guid? SourceExamId { get; set; }

        public Guid TargetExamId { get; set; }

        /// <summary>false = list candidates not yet assigned; true = list students already assigned.</summary>
        public bool EditMode { get; set; }
    }

    public class AtktMatrixResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public AtktPolicyDto? Policy { get; set; }
        public List<AtktSubjectColumnDto> Columns { get; set; } = new();
        public List<AtktStudentRowDto> Students { get; set; } = new();
    }

    public class AtktStudentSelectionDto
    {
        public Guid StdMstId { get; set; }

        /// <summary>Subjects the student is appearing for. Empty removes an existing assignment.</summary>
        public List<Guid> SubjectIds { get; set; } = new();
    }

    public class AtktSaveRequest
    {
        public AtktMatrixRequest Filter { get; set; } = new();
        public List<AtktStudentSelectionDto> Students { get; set; } = new();
    }

    /// <summary>Server-side bulk assign: every eligible student, every selectable subject.</summary>
    public class AtktAssignAllRequest
    {
        public AtktMatrixRequest Filter { get; set; } = new();
    }

    public class AtktDeleteRequest
    {
        public AtktMatrixRequest Filter { get; set; } = new();
        public Guid StdMstId { get; set; }
    }

    public class AtktSaveResultDto
    {
        public int StudentsAssigned { get; set; }
        public int StudentsUpdated { get; set; }
        public int StudentsRemoved { get; set; }
        public int SubjectsRegistered { get; set; }
        public List<string> Skipped { get; set; } = new();
    }

    public class AtktExportRequest
    {
        public AtktMatrixRequest Filter { get; set; } = new();

        /// <summary>"All" = the full applied/not-applied matrix; "SeatNo" = per-subject seat lists.</summary>
        public string ExportType { get; set; } = "All";
    }
}
