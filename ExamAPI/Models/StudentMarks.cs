using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class StudentMarks : BaseEntity
    {
        [Key]
        public Guid Id { get; set; }

        public bool IsCarryForward { get; set; }

        [MaxLength(50)]
        public string? Head { get; set; } // positional key, e.g. "H1"

        public int? RawMarks { get; set; }
        public int? Marks { get; set; }
        public int? Resolution { get; set; }

        /// <summary>The student was absent for this head. Distinct from a null
        /// <see cref="Marks"/>, which means "not entered yet".</summary>
        public bool IsAbsent { get; set; }

        [MaxLength(50)]
        public string? Grace { get; set; }

        [MaxLength(100)]
        public string? Remark { get; set; } // legacy, no longer written; see StudentSubjectResult

        public int? RawGradePoint { get; set; }
        public int? GradePoint { get; set; }

        [MaxLength(10)]
        public string? Grade { get; set; }

        // Foreign Keys
        public Guid? MarksId { get; set; }
        [ForeignKey(nameof(MarksId))]
        public MarksMaster? MarksMaster { get; set; }

        public Guid? SubjectId { get; set; }
        [ForeignKey(nameof(SubjectId))]
        public SubjectMaster? Subject { get; set; }

        public Guid? CreditsId { get; set; }
        [ForeignKey(nameof(CreditsId))]
        public SubjectCreditMaster? CreditMaster { get; set; }
    }
}