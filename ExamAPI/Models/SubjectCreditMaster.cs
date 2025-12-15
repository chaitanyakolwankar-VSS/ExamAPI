using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class SubjectCreditMaster : BaseEntity
    {
        [Key]
        public Guid CreditsId { get; set; }

        [MaxLength(20)]
        public string? TotalCredits { get; set; }

        [MaxLength(20)]
        public string? AYID { get; set; }

        // Foreign Key
        public Guid? SubjectId { get; set; }
        [ForeignKey(nameof(SubjectId))]
        public SubjectMaster? Subject { get; set; }

        // Navigation Property
        public ICollection<SubjectCredits>? Credits { get; set; }
    }
}