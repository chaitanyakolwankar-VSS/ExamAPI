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
        public string? Head { get; set; } // e.g. "Theory"

        public int? Marks { get; set; }
        public int? Resolution { get; set; }

        [MaxLength(50)]
        public string? Grace { get; set; }

        [MaxLength(100)]
        public string? Remark { get; set; }

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