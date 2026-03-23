using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class RuleSet : BaseEntity
    {
        [Key]
        public Guid RuleSetId { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Foreign Key
        public Guid PatternId { get; set; }
        [ForeignKey(nameof(PatternId))]
        public PatternMaster? Pattern { get; set; }

        public Guid? GradeMasterId { get; set; }
        [ForeignKey(nameof(GradeMasterId))]
        public GradeMaster? GradeMaster { get; set; }

        // Navigation Properties
        public ICollection<Rule>? Rules { get; set; }
    }
}