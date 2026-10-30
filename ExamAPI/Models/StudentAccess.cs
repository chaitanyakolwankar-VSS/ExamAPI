using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    // =====================================================================================
    // Student access: staff send a student an emailed link for ONE task (verify Student Master
    // details now; hall ticket, marksheet, ATKT payment later). The student proves the inbox with
    // a 6-digit OTP and gets a short session scoped to that student, college and task.
    // No raw token or OTP is ever stored: only their hashes. Emails that carry one are rendered
    // at send time (EmailQueue.Kind), so the queue never holds a secret either.
    // =====================================================================================

    /// <summary>What a student was asked to do. Platform-wide rows, seeded; new tasks are new rows, not new columns.</summary>
    public class TaskType
    {
        [Key]
        [MaxLength(30)]
        public required string Code { get; set; } // TaskTypes.StudentMaster, ...

        [MaxLength(100)]
        public required string Name { get; set; }

        /// <summary>When a link stops working: <see cref="TaskExpiryRules"/>.</summary>
        [MaxLength(20)]
        public required string ExpiryRule { get; set; }

        public bool RequiresOtp { get; set; } = true;
    }

    public static class TaskTypes
    {
        public const string StudentMaster = "STUDENT_MASTER";
        public const string HallTicket = "HALL_TICKET";
        public const string Marksheet = "MARKSHEET";
        public const string AtktPayment = "ATKT_PAYMENT";
    }

    public static class TaskExpiryRules
    {
        /// <summary>The link dies when the student submits.</summary>
        public const string OnSubmit = "DIE_ON_SUBMIT";
        /// <summary>The link works (and the form stays editable) until the deadline; every visit needs a new OTP.</summary>
        public const string OnDeadline = "DIE_ON_DEADLINE";
        /// <summary>The link dies when the payment is verified.</summary>
        public const string OnPayment = "DIE_ON_PAYMENT";
    }

    /// <summary>One "Send links" action: which task, for which class, until when.</summary>
    public class TaskBatch : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }

        public Guid? CollegeId { get; set; }

        [MaxLength(30)]
        public required string TaskTypeCode { get; set; }

        [MaxLength(200)]
        public string? Title { get; set; }

        /// <summary>Links of this batch expire here (UTC); staff can move it for the whole batch.</summary>
        public DateTime? Deadline { get; set; }

        /// <summary>What the task is about when it is not the student alone, e.g. the ExamId of a hall ticket.</summary>
        public Guid? RefId { get; set; }

        // The class the links were sent to (for the staff list; each link names its own student).
        public Guid? CourseId { get; set; }
        [MaxLength(50)]
        public string? SemesterId { get; set; }
        public Guid? AYID { get; set; }

        /// <summary>Comma-separated <see cref="StudentFields"/> the student may change (Student Master task); the rest are shown read-only.</summary>
        [MaxLength(500)]
        public string? EditableFields { get; set; }

        /// <summary>Where the student site is (e.g. https://www.vivacollege.in/gradesphereapp): links are written with it at send time.</summary>
        [MaxLength(300)]
        public string? LinkBaseUrl { get; set; }
    }

    /// <summary>Student Master fields a details link can let the student change. Student ID and the email never are.</summary>
    public static class StudentFields
    {
        public const string FirstName = "firstName";
        public const string MiddleName = "middleName";
        public const string LastName = "lastName";
        public const string MotherName = "motherName";
        public const string DateOfBirth = "dateOfBirth";
        public const string Gender = "gender";
        public const string Category = "category";
        public const string Phone = "phone";
        public const string Address = "address";
        public const string Prn = "prn";
        public const string AbcId = "abcId";
        public const string Photo = "photo";
        public const string Signature = "signature";

        public static readonly IReadOnlyList<string> All = new[]
        {
            FirstName, MiddleName, LastName, MotherName, DateOfBirth, Gender, Category, Phone, Address, Prn, AbcId, Photo, Signature,
        };

        /// <summary>Known fields only, each once, in the order of <see cref="All"/>.</summary>
        public static List<string> Clean(IEnumerable<string>? fields)
        {
            var wanted = new HashSet<string>((fields ?? Array.Empty<string>()).Select(f => f.Trim()), StringComparer.OrdinalIgnoreCase);
            return All.Where(wanted.Contains).ToList();
        }

        public static List<string> Parse(string? stored) => Clean((stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
    }

    public static class TaskLinkStatuses
    {
        public const string Active = "Active";
        public const string Used = "Used";
        public const string Expired = "Expired";
        public const string Revoked = "Revoked";
    }

    /// <summary>A student's link for one task. CreatedBy (BaseEntity) is the staff user; null means the system.</summary>
    public class TaskLink : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }

        public Guid? CollegeId { get; set; }

        [MaxLength(30)]
        public required string TaskTypeCode { get; set; }
        public TaskType? TaskType { get; set; }

        public Guid? BatchId { get; set; }
        public TaskBatch? Batch { get; set; }

        public Guid StdMstId { get; set; }
        [ForeignKey(nameof(StdMstId))]
        public StudentMaster? Student { get; set; }

        /// <summary>Same as <see cref="TaskBatch.RefId"/>, for a link sent on its own.</summary>
        public Guid? RefId { get; set; }

        /// <summary>SHA-256 of the raw token. Null until the email goes out: the token is made at send time.</summary>
        [Column(TypeName = "binary(32)")]
        public byte[]? TokenHash { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = TaskLinkStatuses.Active;

        public DateTime ExpiresAt { get; set; }
        public DateTime? SentAt { get; set; }
        /// <summary>First time the student signed in with it (OTP checked).</summary>
        public DateTime? OpenedAt { get; set; }
        /// <summary>Last time the student completed the task (details saved, ...).</summary>
        public DateTime? UsedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }

    /// <summary>A 6-digit code sent for a link. Single use, short-lived, limited attempts.</summary>
    public class OtpChallenge : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }

        public Guid? CollegeId { get; set; }

        public Guid TaskLinkId { get; set; }
        public TaskLink? TaskLink { get; set; }

        /// <summary>HMAC-SHA256 of the code with a server key (a bare hash of 6 digits is trivially reversible).</summary>
        [Column(TypeName = "binary(32)")]
        public byte[]? CodeHash { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int AttemptCount { get; set; }
        public DateTime? UsedAt { get; set; }
    }

    /// <summary>A signed-in student, for one task. The cookie holds a random id; only its hash is stored.</summary>
    public class StudentSession : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }

        public Guid? CollegeId { get; set; }

        [Column(TypeName = "binary(32)")]
        public required byte[] SessionHash { get; set; }

        public Guid TaskLinkId { get; set; }
        public TaskLink? TaskLink { get; set; }

        public Guid StdMstId { get; set; }

        [MaxLength(30)]
        public required string TaskTypeCode { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime LastSeenAt { get; set; }
        /// <summary>Sliding: moved forward on use, never past the link's own expiry.</summary>
        public DateTime ExpiresAt { get; set; }
        public DateTime? EndedAt { get; set; }
    }

    public static class EmailKinds
    {
        /// <summary>Body rendered at send time from the OtpChallenge (RefId); a fresh code is made then.</summary>
        public const string Otp = "OTP";
        /// <summary>Body rendered at send time from the TaskLink (RefId); the token is made then.</summary>
        public const string TaskLink = "TASK_LINK";
        /// <summary>Subject and bodies stored as given.</summary>
        public const string Plain = "PLAIN";
    }

    public static class EmailStatuses
    {
        public const string Pending = "Pending";
        public const string Sending = "Sending";
        public const string Sent = "Sent";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
    }

    public static class EmailPriorities
    {
        public const int High = 1; // OTP
        public const int Low = 5;  // bulk task links
    }

    /// <summary>Outgoing mail, sent by the background sender in priority order. Also the per-college daily count (SentAt).</summary>
    public class EmailQueue : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }

        public Guid? CollegeId { get; set; }

        [MaxLength(20)]
        public required string Kind { get; set; }

        /// <summary>The OtpChallenge or TaskLink the body is rendered from.</summary>
        public Guid? RefId { get; set; }

        [MaxLength(255)]
        public required string ToAddress { get; set; }

        [MaxLength(300)]
        public string? Subject { get; set; }
        public string? HtmlBody { get; set; }
        public string? TextBody { get; set; }

        public int Priority { get; set; } = EmailPriorities.Low;

        [MaxLength(20)]
        public string Status { get; set; } = EmailStatuses.Pending;

        public int Attempts { get; set; }

        [MaxLength(1000)]
        public string? LastError { get; set; }

        public DateTime CreatedAt { get; set; }
        /// <summary>Not before this (retry back-off).</summary>
        public DateTime NextAttemptAt { get; set; }
        /// <summary>Claimed by a sender until then; another process skips it (overlapping app pools during a recycle).</summary>
        public DateTime? LockedUntil { get; set; }
        public DateTime? SentAt { get; set; }
    }

    /// <summary>Per-college settings for student email, set by the college admin. The sending account is an <see cref="EmailAccount"/>.</summary>
    public class CollegeSettings : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }

        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? College { get; set; }

        /// <summary>Comma-separated, e.g. "vivatech.in,student.vivatech.in". Empty = any domain is accepted.</summary>
        [MaxLength(500)]
        public string? AllowedEmailDomains { get; set; }

        /// <summary>Shown in every student email footer: "For help, contact ...".</summary>
        [MaxLength(255)]
        public string? ExamOfficeEmail { get; set; }

        public int DailyEmailLimit { get; set; } = 2000;

        /// <summary>Start of every exam letter number: "VIT/EXAM/SUP/2026-27/0042". Empty = "{college code}/EXAM".</summary>
        [MaxLength(40)]
        public string? LetterPrefix { get; set; }

        /// <summary>Contact numbers printed on supervision letters ("CAP contact").</summary>
        [MaxLength(100)]
        public string? CapContact { get; set; }

        /// <summary>Supervision letters: where staff report before a session (e.g. "the Central Examination Control Room").</summary>
        [MaxLength(200)]
        public string? ReportingPlace { get; set; }

        /// <summary>Supervision letters: the guidelines printed as bullets, one per line.</summary>
        [MaxLength(2000)]
        public string? DutyGuidelines { get; set; }

        /// <summary>The end of the letters-link emails sent to staff, one line per line ("Thanks &amp; Regards, ..."). Empty = "Exam Office" and the college name.</summary>
        [MaxLength(1000)]
        public string? LetterEmailSignature { get; set; }
    }

    /// <summary>
    /// The mail account emails are sent from (e.g. a Google account with an app password). Set by the platform
    /// admin only: one per college, and one platform default (CollegeId null) used by colleges without their own.
    /// Not college-scoped on purpose: colleges never read it; only the platform screens and the sender do.
    /// </summary>
    public class EmailAccount
    {
        [Key]
        public Guid Id { get; set; }

        /// <summary>Null = the platform default.</summary>
        public Guid? CollegeId { get; set; }

        /// <summary><see cref="EmailProviders"/>; another provider (an HTTPS mail API) can be added later.</summary>
        [MaxLength(20)]
        public string Provider { get; set; } = EmailProviders.Smtp;

        [MaxLength(255)]
        public required string Host { get; set; }
        public int Port { get; set; } = 587;
        public bool UseStartTls { get; set; } = true;

        [MaxLength(255)]
        public required string Username { get; set; }

        /// <summary>Encrypted with a key derived from the server's Jwt:Key (SecretProtector); never sent to a screen.</summary>
        [MaxLength(1000)]
        public string? PasswordProtected { get; set; }

        [MaxLength(255)]
        public required string FromAddress { get; set; }
        [MaxLength(100)]
        public string? FromName { get; set; }

        public DateTime UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }

    public static class EmailProviders
    {
        public const string Smtp = "SMTP";
    }

    public static class EmailVerification
    {
        public const string Verified = "Verified";
        public const string Flagged = "Flagged";
    }
}
