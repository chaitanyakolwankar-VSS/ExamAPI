using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    // =====================================================================================
    // Staff links: the exam office emails a member of staff one link for ONE exam's letters. The staff member
    // proves the inbox with a 6-digit code, checks their details and downloads their letters (PDF). Same rules as
    // the student links (Models/StudentAccess.cs) in tables of their own: only hashes of the token, the code and
    // the session are stored, and no email queue row holds a secret (the body is written when it is sent).
    // =====================================================================================

    public static class StaffLinkStatuses
    {
        public const string Active = "Active";
        public const string Revoked = "Revoked";
        public const string Expired = "Expired";
    }

    /// <summary>A staff member's link for one exam. At most one is Active per person and exam: sending again revokes the older one.</summary>
    public class StaffLink : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }
        public Guid? CollegeId { get; set; }

        public Guid StaffId { get; set; }
        public StaffMaster? Staff { get; set; }

        /// <summary>The canonical copy of the exam (see ExamGroups): the letters of this exam are the "current" ones.</summary>
        public Guid ExamId { get; set; }

        /// <summary>SHA-256 of the raw token. Null until the email goes out: the token is made at send time.</summary>
        [Column(TypeName = "binary(32)")]
        public byte[]? TokenHash { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = StaffLinkStatuses.Active;

        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>When the token was written into the email.</summary>
        public DateTime? SentAt { get; set; }
        /// <summary>First time the person signed in with it (code checked).</summary>
        public DateTime? OpenedAt { get; set; }
        /// <summary>The person confirmed their details (the "Check your details" step).</summary>
        public DateTime? DetailsConfirmedAt { get; set; }
        /// <summary>First time a letter PDF was downloaded through the link.</summary>
        public DateTime? DownloadedAt { get; set; }
        public DateTime? RevokedAt { get; set; }

        /// <summary>Where the staff site is (e.g. https://www.vivacollege.in/gradesphereapp): the link is written with it when the email is sent.</summary>
        [MaxLength(300)]
        public string? LinkBaseUrl { get; set; }
    }

    /// <summary>A 6-digit code sent for a staff link. Single use, short-lived, limited attempts.</summary>
    public class StaffOtp : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }
        public Guid? CollegeId { get; set; }

        public Guid LinkId { get; set; }
        public StaffLink? Link { get; set; }

        /// <summary>HMAC-SHA256 of the code with a server key (a bare hash of 6 digits is trivially reversible).</summary>
        [Column(TypeName = "binary(32)")]
        public byte[]? CodeHash { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int AttemptCount { get; set; }
        public DateTime? UsedAt { get; set; }
    }

    /// <summary>A signed-in staff member. The browser holds a random id; only its hash is stored.</summary>
    public class StaffSession : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }
        public Guid? CollegeId { get; set; }

        [Column(TypeName = "binary(32)")]
        public required byte[] SessionHash { get; set; }

        public Guid LinkId { get; set; }
        public StaffLink? Link { get; set; }

        public Guid StaffId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime LastSeenAt { get; set; }
        /// <summary>Sliding: moved forward on use, never past the link's own expiry.</summary>
        public DateTime ExpiresAt { get; set; }
        public DateTime? EndedAt { get; set; }
    }

    /// <summary><see cref="EmailQueue.Kind"/> values of staff links (the bodies are written when sent, from the link or the code).</summary>
    public static class StaffEmailKinds
    {
        /// <summary>RefId is the StaffOtp; a fresh code is made when the email is written.</summary>
        public const string Otp = "STAFF_OTP";
        /// <summary>RefId is the StaffLink; the token is made when the email is written.</summary>
        public const string Link = "STAFF_LINK";
    }
}
