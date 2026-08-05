using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class PatternMaster : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid PatternId { get; set; }

        [Required]
        [MaxLength(100)]
        public required string PatternName { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

        // Foreign Key
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? College { get; set; }

        // Navigation Properties
        public ICollection<RuleSet>? RuleSets { get; set; }
    }
}