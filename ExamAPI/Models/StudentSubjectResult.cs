using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    /// <summary>Allowed values for <see cref="StudentSubjectResult.SubjectStatus"/>.</summary>
    public static class SubjectStatuses
    {
        public const string Passed = "Passed";
        public const string Failed = "Failed";
        public const string Absent = "Absent";
    }

    /// <summary>
    /// The computed verdict for one student's one subject, sitting between MarksMaster
    /// (student x exam) and StudentMarks (one row per head).
    /// <para>
    /// Written only by result processing, which upserts one row per (MarksId, SubjectId).
    /// Assign-exam and marks entry never touch it -- they own the raw StudentMarks input.
    /// </para>
    /// </summary>
    public class StudentSubjectResult : BaseEntity
    {
        [Key]
        public Guid Id { get; set; }

        public int ObtainedTotal { get; set; }
        public int RawObtainedTotal { get; set; }
        public int OutOfTotal { get; set; }

        /// <summary>Ordinance grace (@ # *) awarded against the combined deficit. Resolution (^)
        /// is not here -- it is a marks-entry mark bump held on StudentMarks.</summary>
        public int GraceApplied { get; set; }

        [MaxLength(10)]
        public string? GraceSymbol { get; set; }

        [MaxLength(10)]
        public string? Grade { get; set; }

        public int GradePoint { get; set; }
        public int RawGradePoint { get; set; }

        public bool IsPassed { get; set; }

        /// <summary>One of <see cref="SubjectStatuses"/>.</summary>
        [MaxLength(20)]
        public string SubjectStatus { get; set; } = SubjectStatuses.Failed;

        // Foreign Keys
        public Guid MarksId { get; set; }
        [ForeignKey(nameof(MarksId))]
        public MarksMaster? MarksMaster { get; set; }

        public Guid SubjectId { get; set; }
        [ForeignKey(nameof(SubjectId))]
        public SubjectMaster? Subject { get; set; }

        public Guid? CreditsId { get; set; }
        [ForeignKey(nameof(CreditsId))]
        public SubjectCreditMaster? CreditMaster { get; set; }
    }
}
