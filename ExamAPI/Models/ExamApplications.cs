using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    // =====================================================================================
    // Student exam applications (docs/STUDENT_EXAM_APPLICATIONS.md): staff set the fee for an
    // ATKT, Re-exam or Revaluation exam, students apply and pay by link, and a paid application
    // puts the student into the exam. Amounts are always worked out by the server.
    // =====================================================================================

    /// <summary>What kind of follow-up exam an application or fee setup is for (derived from the exam, never typed).</summary>
    public static class ExamApplicationKinds
    {
        public const string Atkt = "ATKT";
        public const string ReExam = "REEXAM";
        public const string Revaluation = "REVAL";

        public static string Label(string kind) => kind switch
        {
            Atkt => "A.T.K.T",
            ReExam => "Re-Exam",
            Revaluation => "Revaluation",
            _ => kind,
        };
    }

    /// <summary>The fee and the application window of one target exam. One per exam.</summary>
    public class ExamFeeSetup : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }

        public Guid? CollegeId { get; set; }

        public Guid ExamId { get; set; }
        [ForeignKey(nameof(ExamId))]
        public ExamMaster? Exam { get; set; }

        /// <summary><see cref="ExamApplicationKinds"/>, from the exam when saved.</summary>
        [MaxLength(10)]
        public required string Kind { get; set; }

        /// <summary>Applications open here (UTC); null = as soon as links are sent.</summary>
        public DateTime? OpensAt { get; set; }

        /// <summary>Applications and payments close here (UTC); links expire with it.</summary>
        public DateTime ClosesAt { get; set; }

        /// <summary>Fee per subject (per paper for revaluation).</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal PerSubjectFee { get; set; }

        /// <summary>Optional cap on the per-subject part ("₹X per subject, at most ₹Y").</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal? MaxSubjectFee { get; set; }

        /// <summary>A fixed amount added once, e.g. the statement of marks.</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal FixedFee { get; set; }

        [MaxLength(100)]
        public string? FixedFeeLabel { get; set; }

        /// <summary>From here (UTC) the late fee is added; null = no late fee.</summary>
        public DateTime? LateFeeFrom { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal LateFee { get; set; }

        /// <summary>Shown to the student above the fee.</summary>
        [MaxLength(1000)]
        public string? Notes { get; set; }

        public List<ExamFeeCategoryRate> CategoryRates { get; set; } = new();
    }

    /// <summary>A different fee for one student category (Student Master "Category"), or no fee at all.</summary>
    public class ExamFeeCategoryRate : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }

        public Guid? CollegeId { get; set; }

        public Guid FeeSetupId { get; set; }

        [MaxLength(50)]
        public required string Category { get; set; }

        /// <summary>No fee for this category: the application is accepted without a payment ("Fee waived").</summary>
        public bool IsExempt { get; set; }

        /// <summary>Null = the setup's own per-subject fee.</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal? PerSubjectFee { get; set; }

        /// <summary>Null = the setup's own fixed fee.</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal? FixedFee { get; set; }
    }

    public static class ExamApplicationStatuses
    {
        /// <summary>Subjects chosen, not paid yet (orders may be open).</summary>
        public const string Draft = "Draft";
        /// <summary>Paid (online, at the office or waived); not yet put into the exam.</summary>
        public const string Paid = "Paid";
        /// <summary>Paid and put into the exam.</summary>
        public const string Assigned = "Assigned";
        /// <summary>Paid, but putting the student into the exam failed; staff retry (see AssignError).</summary>
        public const string AssignFailed = "AssignFailed";
        public const string Cancelled = "Cancelled";
        public const string Refunded = "Refunded";

        public static bool IsPaid(string status) => status is Paid or Assigned or AssignFailed;
    }

    public static class PaidVia
    {
        public const string Online = "Online";
        public const string Office = "Office";
        public const string Waived = "Waived";
    }

    /// <summary>One student's application for one target exam.</summary>
    public class ExamApplication : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }

        public Guid? CollegeId { get; set; }

        public Guid StdMstId { get; set; }
        [ForeignKey(nameof(StdMstId))]
        public StudentMaster? Student { get; set; }

        public Guid TargetExamId { get; set; }
        [ForeignKey(nameof(TargetExamId))]
        public ExamMaster? TargetExam { get; set; }

        /// <summary>Revaluation: the exam whose papers are re-checked.</summary>
        public Guid? SourceExamId { get; set; }

        /// <summary>The class the student applied in (from the link): the assignment needs semester and pattern.</summary>
        [MaxLength(50)]
        public string? SemesterId { get; set; }

        [MaxLength(50)]
        public string? Pattern { get; set; }

        /// <summary>When the student ticked the declaration (the form prints it). Null for staff-made applications.</summary>
        public DateTime? DeclaredAt { get; set; }

        /// <summary>The link the student applied through.</summary>
        public Guid? TaskLinkId { get; set; }

        [MaxLength(10)]
        public required string Kind { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = ExamApplicationStatuses.Draft;

        /// <summary>What the student must pay, worked out by the server from the fee setup.</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal AmountDue { get; set; }

        /// <summary>The lines behind AmountDue, as shown to the student and on the receipt (JSON).</summary>
        [MaxLength(2000)]
        public string? FeeBreakdownJson { get; set; }

        [MaxLength(40)]
        public string? ReceiptNo { get; set; }

        public DateTime? PaidAt { get; set; }

        /// <summary><see cref="Models.PaidVia"/>.</summary>
        [MaxLength(10)]
        public string? PaidVia { get; set; }

        /// <summary>Office payments: the office receipt number or reference.</summary>
        [MaxLength(100)]
        public string? PaymentReference { get; set; }

        public DateTime? AssignedAt { get; set; }

        [MaxLength(1000)]
        public string? AssignError { get; set; }

        /// <summary>Staff notes: cancellation or refund reason, "paid twice", ...</summary>
        [MaxLength(1000)]
        public string? Note { get; set; }

        /// <summary>A second successful payment arrived for this application: staff refund one.</summary>
        public bool PaidTwice { get; set; }

        /// <summary>Changed on every save, so two confirmations of one payment cannot both mark it paid.</summary>
        [ConcurrencyCheck]
        public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

        public List<ExamApplicationSubject> Subjects { get; set; } = new();
    }

    /// <summary>A subject the student applied for (and, for head-wise subjects, which heads).</summary>
    public class ExamApplicationSubject : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }

        public Guid? CollegeId { get; set; }

        public Guid ApplicationId { get; set; }

        public Guid SubjectId { get; set; }

        /// <summary>Comma-separated head keys (H1,H2); null = the whole subject.</summary>
        [MaxLength(200)]
        public string? Heads { get; set; }
    }

    public static class PaymentOrderStatuses
    {
        /// <summary>Made; the student has not come back from the gateway yet.</summary>
        public const string Created = "Created";
        /// <summary>The gateway has not decided yet; re-checked in the background.</summary>
        public const string Pending = "Pending";
        public const string Success = "Success";
        public const string Failed = "Failed";
        /// <summary>No answer within the re-check window.</summary>
        public const string Expired = "Expired";
        /// <summary>The gateway says paid, but not the amount or order we asked for: never counted, staff check it.</summary>
        public const string Mismatch = "Mismatch";

        public static bool IsOpen(string status) => status is Created or Pending;
    }

    /// <summary>One attempt to pay an application. A failed attempt is kept; the student tries again with a new order.</summary>
    public class PaymentOrder : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }

        public Guid? CollegeId { get; set; }

        public Guid ApplicationId { get; set; }
        [ForeignKey(nameof(ApplicationId))]
        public ExamApplication? Application { get; set; }

        /// <summary>What the gateway knows the payment by. Random, never built from counts.</summary>
        [MaxLength(40)]
        public required string OrderNo { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        [MaxLength(3)]
        public string Currency { get; set; } = "INR";

        public Guid? PaymentAccountId { get; set; }

        [MaxLength(20)]
        public required string Provider { get; set; }

        public bool IsTestMode { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = PaymentOrderStatuses.Created;

        [MaxLength(100)]
        public string? GatewayTxnId { get; set; }

        [MaxLength(50)]
        public string? Mode { get; set; }

        [MaxLength(100)]
        public string? Bank { get; set; }

        /// <summary>The gateway's own status word, for staff.</summary>
        [MaxLength(100)]
        public string? GatewayStatus { get; set; }

        [MaxLength(500)]
        public string? Message { get; set; }

        /// <summary>Where the student goes after the gateway (the student site, checked against the allowed origins).</summary>
        [MaxLength(300)]
        public string? ReturnBaseUrl { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public DateTime? LastCheckedAt { get; set; }
        public DateTime? NextCheckAt { get; set; }
        public int CheckCount { get; set; }

        [ConcurrencyCheck]
        public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    }

    /// <summary>
    /// A gateway merchant account: a college's own, or the platform default (CollegeId null) for colleges without one.
    /// Keys are stored encrypted (SecretProtector) and never sent to a screen.
    /// </summary>
    public class PaymentAccount
    {
        [Key]
        public Guid Id { get; set; }

        /// <summary>Null = the platform default.</summary>
        public Guid? CollegeId { get; set; }

        /// <summary><see cref="PaymentProviders"/>.</summary>
        [MaxLength(20)]
        public required string Provider { get; set; }

        [MaxLength(100)]
        public string? MerchantId { get; set; }

        /// <summary>The provider's keys as JSON, encrypted.</summary>
        [MaxLength(4000)]
        public string? SecretsProtected { get; set; }

        public bool IsTestMode { get; set; } = true;

        public bool IsEnabled { get; set; } = true;

        public DateTime UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }

    public static class PaymentProviders
    {
        /// <summary>Built-in pretend gateway for development and the demo; refused in Production.</summary>
        public const string Test = "TEST";
    }

    /// <summary>Running receipt numbers per college and series (e.g. "ATKT/2026-27").</summary>
    public class ReceiptCounter
    {
        public Guid CollegeId { get; set; }

        [MaxLength(40)]
        public required string Series { get; set; }

        /// <summary>The last number given out.</summary>
        [ConcurrencyCheck]
        public int LastNo { get; set; }
    }
}
