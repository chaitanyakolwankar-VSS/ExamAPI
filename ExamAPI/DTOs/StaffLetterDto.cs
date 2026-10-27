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
        public int LetterCount { get; set; }
    }

    /// <summary>A date and time on which at least one paper is written.</summary>
    public class SupervisionSlotDto
    {
        public string Date { get; set; } = "";
        public string Time { get; set; } = "";
        public int Papers { get; set; }
        public List<string> Branches { get; set; } = new();
    }

    public class DutyDto
    {
        public string Date { get; set; } = "";
        public string Time { get; set; } = "";
    }

    public class SupervisionLetterDto
    {
        public Guid LetterId { get; set; }
        public Guid StaffId { get; set; }
        public string StaffName { get; set; } = "";
        public string LetterNo { get; set; } = "";
        /// <summary>yyyy-MM-dd.</summary>
        public string LetterDate { get; set; } = "";
        public string Role { get; set; } = "";
        public List<DutyDto> Duties { get; set; } = new();
    }

    /// <summary>Everything the Supervision Letters screen needs for one exam.</summary>
    public class SupervisionDto
    {
        public ExamGroupDto Exam { get; set; } = new();
        public List<string> Roles { get; set; } = new();
        public List<SupervisionSlotDto> Slots { get; set; } = new();
        public List<SupervisionLetterDto> Letters { get; set; } = new();
    }

    public class SupervisionAssignment
    {
        public Guid StaffId { get; set; }
        public string? Role { get; set; }
        public List<DutyDto> Duties { get; set; } = new();
    }

    /// <summary>The whole assignment grid of one exam: people missing from it lose their letter.</summary>
    public class SupervisionSave
    {
        /// <summary>Any branch's copy of the exam.</summary>
        public Guid ExamId { get; set; }
        /// <summary>yyyy-MM-dd; given to the new letters only.</summary>
        public string? LetterDate { get; set; }
        public List<SupervisionAssignment> Assignments { get; set; } = new();
    }

    public class SupervisionSaveResult
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int Removed { get; set; }
        public string Message { get; set; } = "";
        public List<SupervisionLetterDto> Letters { get; set; } = new();
    }

    public class LetterSettingsDto
    {
        public string? LetterPrefix { get; set; }
        public string? CapContact { get; set; }
        /// <summary>What the prefix is when none is set: "{college code}/EXAM".</summary>
        public string DefaultPrefix { get; set; } = "";
    }

    public class LetterSettingsSave
    {
        public string? LetterPrefix { get; set; }
        public string? CapContact { get; set; }
    }
}
