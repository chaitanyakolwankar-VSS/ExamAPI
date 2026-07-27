using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace ExamAPI.Models
{
    public class GradeMaster : BaseEntity, ICollegeTemplatable
    {

        /// <summary>Tenant owner. See <see cref="ICollegeScoped"/>.</summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }
        [Key]
        public Guid GradeMasterId { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        [MaxLength(200)]
        public string? Description { get; set; }

        // Navigation Properties
        public ICollection<GradeThreshold>? Thresholds { get; set; }
        public ICollection<RuleSet>? RuleSets { get; set; }
    }
}