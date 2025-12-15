using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class RuleCondition : BaseEntity
    {
        [Key]
        public Guid ConditionId { get; set; }

        [MaxLength(100)]
        public string? FactName { get; set; } // e.g. "TotalMarks"

        [MaxLength(20)]
        public string? Operator { get; set; } // e.g. ">", "<", "=="

        [MaxLength(100)]
        public string? Value { get; set; } // e.g. "40"

        // Foreign Key
        public Guid? RuleId { get; set; }
        [ForeignKey(nameof(RuleId))]
        public Rule? Rule { get; set; }
    }
}