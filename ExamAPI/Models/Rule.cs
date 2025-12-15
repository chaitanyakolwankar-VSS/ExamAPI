using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class Rule : BaseEntity
    {
        [Key]
        public Guid RuleId { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        public int Priority { get; set; }
        public bool IsEnabled { get; set; }

        // Foreign Key
        public Guid? RuleSetId { get; set; }
        [ForeignKey(nameof(RuleSetId))]
        public RuleSet? RuleSet { get; set; }

        // Navigation Properties
        public ICollection<RuleCondition>? Conditions { get; set; }
        public ICollection<RuleAction>? Actions { get; set; }
    }
}