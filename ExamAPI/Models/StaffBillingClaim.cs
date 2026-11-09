using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ExamAPI.Models
{
    /// <summary>
    /// Master header for a faculty or visiting moderator examination remuneration claim.
    /// Tracks submission, leave days, supervision duties, allowances, bank credit, and approval lifecycle.
    /// </summary>
    public class StaffBillingClaim : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? CollegeId { get; set; }

        /// <summary>Secure random token for faculty / moderator direct claim link.</summary>
        [MaxLength(100)]
        public string Token { get; set; } = string.Empty;

        // Optional links to existing masters
        public Guid? DutyChartId { get; set; }
        public Guid? StaffId { get; set; }
        public Guid? PriceSetId { get; set; }
        public Guid? AYID { get; set; }

        [MaxLength(150)]
        public string SessionName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string StaffName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string EmployeeCode { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Department { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Designation { get; set; } = string.Empty;

        [MaxLength(150)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(30)]
        public string? Phone { get; set; }

        /// <summary>"supervisor", "examiner", "moderator", or "external".</summary>
        [MaxLength(30)]
        public string RoleType { get; set; } = "supervisor";

        public bool IsExternal { get; set; } = false;

        [MaxLength(200)]
        public string? VisitingCollegeName { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        // Supervision duties
        public int AssignedSeniorSupDays { get; set; } = 0;
        public int AttendedSeniorSupDays { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal SeniorSupRate { get; set; } = 250m;

        [MaxLength(200)]
        public string? AssignedSeniorSupDates { get; set; }

        public int AssignedJuniorSupDays { get; set; } = 0;
        public int AttendedJuniorSupDays { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal JuniorSupRate { get; set; } = 150m;

        [MaxLength(200)]
        public string? AssignedJuniorSupDates { get; set; }

        // Moderator / External allowances
        public int TravelDays { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal TravelRate { get; set; } = 200m;

        [MaxLength(200)]
        public string? TravelDates { get; set; }

        public int CapDays { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal CapRate { get; set; } = 175m;

        // Incidentals / Other Sanctioned Expenses
        [Column(TypeName = "decimal(10,2)")]
        public decimal OtherAmount { get; set; } = 0m;

        [MaxLength(500)]
        public string? OtherDescription { get; set; }

        // Bank credentials
        [MaxLength(50)]
        public string BankAccNo { get; set; } = string.Empty;

        [MaxLength(100)]
        public string BankName { get; set; } = string.Empty;

        [MaxLength(30)]
        public string IfscCode { get; set; } = string.Empty;

        // Grand totals
        [Column(TypeName = "decimal(10,2)")]
        public decimal GrandTotal { get; set; } = 0m;

        [MaxLength(300)]
        public string AmountInWords { get; set; } = string.Empty;

        // Lifecycle: "NOT_SENT", "LINK_SENT", "SUBMITTED", "APPROVED", "REJECTED"
        [MaxLength(30)]
        public string Status { get; set; } = "NOT_SENT";

        [MaxLength(50)]
        public string? SavedBillNo { get; set; }

        public DateTime? SentAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }

        [MaxLength(100)]
        public string? ApprovedBy { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [MaxLength(500)]
        public string? AdminRemarks { get; set; }

        // OTP verification for secure direct faculty submission
        [MaxLength(100)]
        [JsonIgnore]
        public string? OtpCodeHash { get; set; }
        [JsonIgnore]
        public DateTime? OtpExpiresAt { get; set; }
        [JsonIgnore]
        public DateTime? OtpSentAt { get; set; }
        [JsonIgnore]
        public int OtpAttempts { get; set; } = 0;
        [JsonIgnore]
        public DateTime? OtpVerifiedAt { get; set; }

        // Multi-subject aggregate convenience accessors for frontend synchronization
        [NotMapped]
        public int PaperCount
        {
            get => PaperItems?.Sum(p => p.TheoryCount) ?? 0;
            set { }
        }

        [NotMapped]
        public string PaperSubject
        {
            get => PaperItems != null && PaperItems.Count > 0 ? string.Join(", ", PaperItems.Select(p => p.SubjectName).Where(s => !string.IsNullOrEmpty(s))) : "";
            set { }
        }

        [NotMapped]
        public string PaperSemester
        {
            get => PaperItems?.FirstOrDefault()?.Semester ?? "Sem-3";
            set { }
        }

        [NotMapped]
        public decimal PaperRate
        {
            get => PaperItems?.FirstOrDefault()?.TheoryRate ?? 16m;
            set { }
        }

        [NotMapped]
        public int TwCount
        {
            get => PaperItems?.Sum(p => p.TwCount) ?? 0;
            set { }
        }

        [NotMapped]
        public decimal TwRate
        {
            get => PaperItems?.FirstOrDefault()?.TwRate ?? 6m;
            set { }
        }

        [NotMapped]
        public int PracticalCount
        {
            get => PaperItems?.Sum(p => p.PracticalCount) ?? 0;
            set { }
        }

        [NotMapped]
        public decimal PracticalRate
        {
            get => PaperItems?.FirstOrDefault()?.PracticalRate ?? 10m;
            set { }
        }

        [NotMapped]
        public int OralCount
        {
            get => PaperItems?.Sum(p => p.OralCount) ?? 0;
            set { }
        }

        [NotMapped]
        public decimal OralRate
        {
            get => PaperItems?.FirstOrDefault()?.OralRate ?? 10m;
            set { }
        }

        [NotMapped]
        public int ProjectOralCount
        {
            get => PaperItems?.Sum(p => p.ProjectOralCount) ?? 0;
            set { }
        }

        [NotMapped]
        public decimal ProjectOralRate
        {
            get => PaperItems?.FirstOrDefault()?.ProjectOralRate ?? 25m;
            set { }
        }

        [NotMapped]
        public int MeCount
        {
            get => PaperItems?.Sum(p => p.MeCount) ?? 0;
            set { }
        }

        [NotMapped]
        public decimal MeRate
        {
            get => PaperItems?.FirstOrDefault()?.MeRate ?? 100m;
            set { }
        }

        [NotMapped]
        public int OwpCount
        {
            get => PaperItems?.Sum(p => p.PracticalCount) ?? 0;
            set { }
        }

        [NotMapped]
        public decimal OwpRate
        {
            get => PaperItems?.FirstOrDefault()?.PracticalRate ?? 10m;
            set { }
        }

        [NotMapped]
        public int TtCount
        {
            get => PaperItems?.Sum(p => p.TtCount) ?? 0;
            set { }
        }

        [NotMapped]
        public decimal TtRate
        {
            get => PaperItems?.FirstOrDefault()?.TtRate ?? 4m;
            set { }
        }

        [NotMapped]
        public bool ShowPricingToStaff { get; set; } = true;

        // Navigational collection of multi-subject items
        public ICollection<StaffBillItem> PaperItems { get; set; } = new List<StaffBillItem>();
    }
}
