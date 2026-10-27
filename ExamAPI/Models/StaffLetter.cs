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

        /// <summary>The group's canonical copy of the exam (see ExamGroups), whichever branch's copy the screen sent.</summary>
        public Guid ExamId { get; set; }
        /// <summary>The exam's academic year; the serial number runs per year.</summary>
        public Guid AYID { get; set; }

        /// <summary>Examiner / moderator letters only; null for supervision.</summary>
        public Guid? SubjectId { get; set; }

        public int SerialNo { get; set; }
        [MaxLength(60)]
        public required string LetterNo { get; set; }
        /// <summary>Date only.</summary>
        public DateTime LetterDate { get; set; }

        /// <summary>Supervision duty role (SupervisionRoles).</summary>
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

    /// <summary>One date and time a supervisor is on duty, copied from the timetable slot. Replaced wholesale when the letter is saved.</summary>
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
            Supervision => "SUP",
            Examiner => "EXM",
            Moderator => "MOD",
            _ => type
        };
    }

    public static class SupervisionRoles
    {
        public const string Senior = "Senior Supervisor";
        public const string Junior = "Junior Supervisor";
        public const string ChiefConductor = "Chief Conductor";
        public const string Peon = "Peon";

        public static readonly IReadOnlyList<string> All = new[] { Senior, Junior, ChiefConductor, Peon };
    }
}
