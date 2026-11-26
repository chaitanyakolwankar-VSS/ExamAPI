using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ExamAPI.Models
{
    /// <summary>
    /// Itemized subject evaluation line item under a StaffBillingClaim.
    /// Conforms to legacy Mumbai University format: Theory, TW, Practical, Oral, and Term Test.
    /// </summary>
    public class StaffBillItem : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? CollegeId { get; set; }

        // Foreign Key to parent Claim
        public Guid ClaimId { get; set; }

        [JsonIgnore]
        public StaffBillingClaim? Claim { get; set; }

        [MaxLength(150)]
        public string SubjectName { get; set; } = string.Empty;

        [NotMapped]
        public string Subject
        {
            get => SubjectName;
            set => SubjectName = string.IsNullOrWhiteSpace(value) ? SubjectName : value;
        }

        [NotMapped]
        public decimal Total
        {
            get => ItemSubtotal;
            set => ItemSubtotal = value;
        }

        [MaxLength(30)]
        public string Semester { get; set; } = "Sem-3";

        // 1. Theory Paper Assessment (or Moderation for moderators)
        public int TheoryCount { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal TheoryRate { get; set; } = 16m;

        // 2. Term Work (TW)
        public int TwCount { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal TwRate { get; set; } = 6m;

        // 3. Practical Examination
        public int PracticalCount { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal PracticalRate { get; set; } = 10m;

        // 4. Oral Examination
        public int OralCount { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal OralRate { get; set; } = 10m;

        public int ProjectOralCount { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal ProjectOralRate { get; set; } = 25m;

        public int MeCount { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal MeRate { get; set; } = 100m;

        // Legacy alias / backward-compatibility count
        public int OwpCount { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal OwpRate { get; set; } = 10m;

        // 5. Term Test (Internal Assessment IA)
        public int TtCount { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal TtRate { get; set; } = 4m;

        // Line Subtotal
        [Column(TypeName = "decimal(10,2)")]
        public decimal ItemSubtotal { get; set; } = 0m;
    }
}
