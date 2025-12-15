using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class SubjectCredits : BaseEntity
    {
        [Key]
        public Guid Id { get; set; }

        [MaxLength(50)]
        public string? Head { get; set; } // e.g., "Theory", "Practical"

        [MaxLength(50)]
        public string? HeadType { get; set; }

        [MaxLength(20)]
        public string? HeadOutOf { get; set; } // Max marks

        [MaxLength(20)]
        public string? HeadPass { get; set; } // Passing marks

        [MaxLength(50)]
        public string? HeadResolution { get; set; }

        [MaxLength(100)]
        public string? HeadFormula { get; set; }

        // Foreign Key
        public Guid? CreditsId { get; set; }
        [ForeignKey(nameof(CreditsId))]
        public SubjectCreditMaster? CreditMaster { get; set; }
    }
}