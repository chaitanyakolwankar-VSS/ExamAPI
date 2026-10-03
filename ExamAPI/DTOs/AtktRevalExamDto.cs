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

    /// <summary>
    /// One head of a subject as it stands for a single student: the mark they hold and whether
    /// that head may be / is selected for a fresh attempt. Populated for every subject the student
    /// actually has. Combined subjects carry heads too, but the UI offers a single subject-level
    /// choice for them; head-wise subjects offer these per head.
    /// </summary>
    public class AtktCellHeadDto
    {
        public string Head { get; set; } = string.Empty;      // positional key, "H1"
        public string HeadType { get; set; } = string.Empty;  // printed label, "ESE"
        public int? Obtained { get; set; }
        public int OutOf { get; set; }
        public int Pass { get; set; }
        public bool IsAbsent { get; set; }

        /// <summary>Below its own passing marks. Only meaningful for a head-wise subject.</summary>
        public bool IsFailing { get; set; }

        public bool Selectable { get; set; }
        public bool Selected { get; set; }
    }

    /// <summary>Per-subject state for one student.</summary>
    public class AtktCellDto
    {
        public Guid SubjectId { get; set; }
        public Guid CreditsId { get; set; }

        /// <summary>How this subject is judged -- "HeadWise" or "Combined". Drives whether the UI
        /// offers per-head selection (head-wise) or one subject-level choice (combined).</summary>
        public string PassingStrategy { get; set; } = string.Empty;

        /// <summary>Per-head marks and selection. The head-wise selection surface.</summary>
        public List<AtktCellHeadDto> Heads { get; set; } = new();

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
        public string? SourceSelectionReason { get; set; }

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

    /// <summary>A subject the student is appearing for, with the exact heads being re-sat.</summary>
    public class AtktSubjectSelectionDto
    {
        public Guid SubjectId { get; set; }

        /// <summary>
        /// Heads to re-sit, by positional key ("H1") or printed label ("ESE"). Empty means the
        /// whole subject -- every head is a fresh attempt. Combined subjects always send empty.
        /// </summary>
        public List<string> Heads { get; set; } = new();
    }

    public class AtktStudentSelectionDto
    {
        public Guid StdMstId { get; set; }

        /// <summary>
        /// Subject-level selection (every head of each subject re-sat). Kept for the matrix view
        /// and combined subjects. Empty (with <see cref="Subjects"/> also empty) removes an
        /// existing assignment.
        /// </summary>
        public List<Guid> SubjectIds { get; set; } = new();

        /// <summary>
        /// Head-level selection from the tile view. When present it takes precedence over
        /// <see cref="SubjectIds"/> for the subjects it names; other subjects still honour SubjectIds.
        /// </summary>
        public List<AtktSubjectSelectionDto> Subjects { get; set; } = new();
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
