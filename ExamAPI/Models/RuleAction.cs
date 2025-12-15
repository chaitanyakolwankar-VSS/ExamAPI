using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class RuleAction : BaseEntity
    {
        [Key]
        public Guid ActionId { get; set; }

        [MaxLength(50)]
        public string? ActionType { get; set; }

        [MaxLength(50)]
        public string? CalculationMode { get; set; }

        [MaxLength(50)]
        public string? Param1Type { get; set; }

        [Column(TypeName = "decimal(18,2)")] // SQL Decimal precision
        public decimal? Param1Value { get; set; }

        [MaxLength(50)]
        public string? Param2Type { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Param2Value { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MaxLimit { get; set; }

        public int? MaxTargetCount { get; set; }

        [MaxLength(100)]
        public string? Target { get; set; }

        // Foreign Key
        public Guid? RuleId { get; set; }
        [ForeignKey(nameof(RuleId))]
        public Rule? Rule { get; set; }
    }
}