using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    /// <summary>
    /// Master rate template for exam duties and evaluation remuneration.
    /// Supports college-specific overrides or platform-wide default circular rates (CollegeId == null).
    /// </summary>
    public class BillingPriceSet : BaseEntity, ICollegeScoped, ICollegeTemplatable
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? CollegeId { get; set; }

        [MaxLength(150)]
        public string Name { get; set; } = "Standard University Rates";

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// When true: faculty sees section rates and multiplied remuneration amounts.
        /// When false: faculty only sees and enters duty counts (monetary amounts hidden).
        /// </summary>
        public bool ShowPricingToStaff { get; set; } = true;

        // Supervision Rates
        [Column(TypeName = "decimal(10,2)")]
        public decimal SeniorSupRate { get; set; } = 250m;

        [Column(TypeName = "decimal(10,2)")]
        public decimal JuniorSupRate { get; set; } = 150m;

        // Theory Assessment & Moderation
        [Column(TypeName = "decimal(10,2)")]
        public decimal TheoryAssessmentRate { get; set; } = 16m;

        [Column(TypeName = "decimal(10,2)")]
        public decimal ModerationRate { get; set; } = 20m;

        // Subject Evaluation Components (as per legacy forms reference)
        [Column(TypeName = "decimal(10,2)")]
        public decimal TwRate { get; set; } = 6m;

        [Column(TypeName = "decimal(10,2)")]
        public decimal PracticalRate { get; set; } = 10m;

        [Column(TypeName = "decimal(10,2)")]
        public decimal OralRate { get; set; } = 10m;

        [Column(TypeName = "decimal(10,2)")]
        public decimal ProjectOralRate { get; set; } = 25m;

        [Column(TypeName = "decimal(10,2)")]
        public decimal MeRate { get; set; } = 100m;

        [Column(TypeName = "decimal(10,2)")]
        public decimal OwpRate { get; set; } = 10m;

        [Column(TypeName = "decimal(10,2)")]
        public decimal TtRate { get; set; } = 4m;

        // Visiting Moderator & External Allowances
        [Column(TypeName = "decimal(10,2)")]
        public decimal TravelAllowanceRate { get; set; } = 200m;

        [Column(TypeName = "decimal(10,2)")]
        public decimal CapAllowanceRate { get; set; } = 175m;
    }
}
