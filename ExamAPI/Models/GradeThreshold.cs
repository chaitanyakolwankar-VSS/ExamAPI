using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class GradeThreshold : BaseEntity
    {
        [Key]
        public Guid ThresholdId { get; set; }

        [Required]
        [MaxLength(10)]
        public required string Grade { get; set; } // e.g., "O", "A+"

        public int GradePoint { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal MinPercentage { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal MaxPercentage { get; set; }

        [MaxLength(50)]
        public string? PerformanceRemark { get; set; } // e.g., "Outstanding"

        // Foreign Key
        public Guid GradeMasterId { get; set; }
        [ForeignKey(nameof(GradeMasterId))]
        public GradeMaster? GradeMaster { get; set; }
    }
}