using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    /// <summary>Allowed values for <see cref="SubjectCreditMaster.PassingStrategy"/>.</summary>
    public static class PassingStrategies
    {
        public const string HeadWise = "HeadWise";
        public const string Combined = "Combined";
    }

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

        /// <summary>How this subject decides pass/fail: "HeadWise" (every head clears its own
        /// HeadPass) or "Combined" (only the sum must clear <see cref="PassPercentage"/>).</summary>
        [MaxLength(20)]
        public string PassingStrategy { get; set; } = PassingStrategies.HeadWise;

        /// <summary>Combined pass threshold as a percentage of the subject's total out-of.
        /// Read only when <see cref="PassingStrategy"/> is "Combined".</summary>
        public int? PassPercentage { get; set; }

        // Foreign Key
        public Guid? SubjectId { get; set; }
        [ForeignKey(nameof(SubjectId))]
        public SubjectMaster? Subject { get; set; }

        // Navigation Property
        public ICollection<SubjectCredits>? Credits { get; set; }
    }
}