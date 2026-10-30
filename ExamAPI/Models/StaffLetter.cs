using System.ComponentModel.DataAnnotations;

namespace ExamAPI.Models
{
    /// <summary>
    /// A numbered letter to one member of staff: a supervision duty for an exam, or an examiner / moderator
    /// appointment for a subject. The name, exam title and half are copied in when the letter is first written, so
    /// a later download prints exactly what was sent. Numbers are never reused: a cancelled letter is soft-deleted.
    /// </summary>
    public class StaffLetter : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }
        public Guid? CollegeId { get; set; }

        public Guid StaffId { get; set; }
        public StaffMaster? Staff { get; set; }

        [MaxLength(20)]
        public required string LetterType { get; set; }   // StaffLetterTypes

        /// <summary>Examiner / moderator letters: the group's canonical copy of the exam (see ExamGroups). Null for supervision.</summary>
        public Guid? ExamId { get; set; }
        /// <summary>Supervision letters: the duty chart (exam period) the letter is for. Null for examiner / moderator.</summary>
        public Guid? DutyChartId { get; set; }
        /// <summary>The academic year; the serial number runs per year.</summary>
        public Guid AYID { get; set; }

        /// <summary>Examiner / moderator letters only; null for supervision.</summary>
        public Guid? SubjectId { get; set; }

        public int SerialNo { get; set; }
        [MaxLength(60)]
        public required string LetterNo { get; set; }
        /// <summary>Date only.</summary>
        public DateTime LetterDate { get; set; }

        /// <summary>Supervision: the highest role among the duties (printed in the title). Examiner / Moderator otherwise.</summary>
        [MaxLength(40)]
        public string? Role { get; set; }

        [MaxLength(200)]
        public required string StaffName { get; set; }
        [MaxLength(200)]
        public required string ExamTitle { get; set; }
        [MaxLength(20)]
        public string Half { get; set; } = "";

        public ICollection<StaffLetterDuty> Duties { get; set; } = new List<StaffLetterDuty>();
    }

    /// <summary>One line of a supervision letter: a day, its session time and the role (or an other-work day), copied from the duty chart. Replaced wholesale when the letter is saved.</summary>
    public class StaffLetterDuty : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }
        public Guid? CollegeId { get; set; }

        public Guid LetterId { get; set; }
        public StaffLetter? Letter { get; set; }

        [MaxLength(20)]
        public required string Date { get; set; }
        [MaxLength(50)]
        public required string Time { get; set; }
        /// <summary>DutyKinds: duty or other.</summary>
        [MaxLength(20)]
        public string Kind { get; set; } = DutyKinds.Duty;
        /// <summary>SupervisionRoles for a duty; a short label for other work.</summary>
        [MaxLength(40)]
        public string? Role { get; set; }
        public int SortOrder { get; set; }
    }

    public static class StaffLetterTypes
    {
        public const string Supervision = "SUPERVISION";
        public const string Examiner = "EXAMINER";
        public const string Moderator = "MODERATOR";

        /// <summary>The letter type's part of the letter number.</summary>
        public static string Code(string type) => type switch
        {
            Supervision => "DUTY",
            Examiner => "EXM",
            Moderator => "MOD",
            _ => type
        };
    }

    public static class SupervisionRoles
    {
        public const string ChiefConductor = "Chief Conductor";
        public const string Senior = "Senior Supervisor";
        public const string Junior = "Junior Supervisor";
        public const string NonTeaching = "Non-Teaching Staff";

        /// <summary>Highest first: a letter's title uses the highest role among its duties.</summary>
        public static readonly IReadOnlyList<string> All = new[] { ChiefConductor, Senior, Junior, NonTeaching };

        /// <summary>The role a new duty gets: Junior Supervisor for teaching staff, Non-Teaching Staff otherwise.</summary>
        public static string DefaultFor(StaffMaster staff) => staff.IsTeaching ? Junior : NonTeaching;
    }
}
