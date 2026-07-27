using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class SubjectCreditMaster : BaseEntity, ICollegeScoped
    {

        /// <summary>Tenant owner. See <see cref="ICollegeScoped"/>.</summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }
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