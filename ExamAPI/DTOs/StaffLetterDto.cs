namespace ExamAPI.DTOs
{
    /// <summary>One exam as the exam office means it: every branch's copy together (see ExamGroups).</summary>
    public class ExamGroupDto
    {
        /// <summary>The group's canonical copy; letters are stored against it whichever copy was asked for.</summary>
        public Guid ExamId { get; set; }
        public string Name { get; set; } = "";
        public string ExamType { get; set; } = "";
        /// <summary>"{Name} {ExamType}", as the letters print it.</summary>
        public string Title { get; set; } = "";
        /// <summary>"First Half" (odd semesters), "Second Half" (even) or empty (mixed or unknown).</summary>
        public string Half { get; set; } = "";
        /// <summary>Branches that have papers on the timetable.</summary>
        public int BranchCount { get; set; }
        public int SlotCount { get; set; }
        /// <summary>Always 0: supervision letters belong to duty charts, not to one exam.</summary>
        public int LetterCount { get; set; }
        /// <summary>Examiner and moderator letters of the exam.</summary>
        public int AppointmentCount { get; set; }
    }

    /// <summary>A subject on the exam's timetable, which an examiner and a moderator can be appointed for.</summary>
    public class AppointmentSubjectDto
    {
        public Guid SubjectId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Branch { get; set; } = "";
        public Guid? CourseId { get; set; }
        /// <summary>The semester number (6 for Semester VI); null when the subject has none.</summary>
        public int? Semester { get; set; }
        public string Pattern { get; set; } = "";
    }

    public class AppointmentLetterDto
    {
        public Guid LetterId { get; set; }
        public Guid StaffId { get; set; }
        public string StaffName { get; set; } = "";
        public string LetterNo { get; set; } = "";
        /// <summary>yyyy-MM-dd.</summary>
        public string LetterDate { get; set; } = "";
        /// <summary>"EXAMINER" or "MODERATOR".</summary>
        public string Type { get; set; } = "";
        public Guid? SubjectId { get; set; }
    }

    /// <summary>Everything the Examiner Letters screen needs for one exam.</summary>
    public class AppointmentsDto
    {
        public ExamGroupDto Exam { get; set; } = new();
        public List<AppointmentSubjectDto> Subjects { get; set; } = new();
        public List<AppointmentLetterDto> Letters { get; set; } = new();
    }

    public class AppointmentSubjectSave
    {
        public Guid SubjectId { get; set; }
        public List<Guid> Examiners { get; set; } = new();
        public List<Guid> Moderators { get; set; } = new();
    }

    /// <summary>The whole Examiner Letters screen of one exam: appointments missing from it are cancelled.</summary>
    public class AppointmentSave
    {
        /// <summary>Any branch's copy of the exam.</summary>
        public Guid ExamId { get; set; }
        /// <summary>yyyy-MM-dd; given to the new letters only.</summary>
        public string? LetterDate { get; set; }
        public List<AppointmentSubjectSave> Subjects { get; set; } = new();
    }

    public class AppointmentSaveResult
    {
        public int Created { get; set; }
        public int Unchanged { get; set; }
        public int Removed { get; set; }
        public string Message { get; set; } = "";
        public List<AppointmentLetterDto> Letters { get; set; } = new();
    }

    public class LetterSettingsDto
    {
        public string? LetterPrefix { get; set; }
        public string? CapContact { get; set; }
        /// <summary>Where staff report before a session; empty = the default.</summary>
        public string? ReportingPlace { get; set; }
        /// <summary>The guidelines printed on duty letters, one per line; empty = the defaults.</summary>
        public string? DutyGuidelines { get; set; }
        /// <summary>The end of the letter emails to staff, one line per line; empty = "Exam Office" and the college name.</summary>
        public string? LetterEmailSignature { get; set; }
        /// <summary>What the prefix is when none is set: "{college code}/EXAM".</summary>
        public string DefaultPrefix { get; set; } = "";
        public string DefaultReportingPlace { get; set; } = "";
        public List<string> DefaultGuidelines { get; set; } = new();
    }

    public class LetterSettingsSave
    {
        public string? LetterPrefix { get; set; }
        public string? CapContact { get; set; }
        /// <summary>At most 200 characters.</summary>
        public string? ReportingPlace { get; set; }
        /// <summary>One guideline per line: at most 10 lines of 300 characters, 2000 in all.</summary>
        public string? DutyGuidelines { get; set; }
        /// <summary>At most 1000 characters; line ends are tidied and empty lines at the start and end dropped.</summary>
        public string? LetterEmailSignature { get; set; }
    }
}
