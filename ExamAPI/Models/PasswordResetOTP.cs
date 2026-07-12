using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class PasswordResetOTP
    {
        public int Id { get; set; }
        public Guid UserId { get; set; }
        public string OTP { get; set; } = string.Empty;
        public DateTime ExpiryTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsUsed { get; set; }
        public int AttemptCount { get; set; }
        [ForeignKey(nameof(UserId))]
        public UserMaster? UserMaster { get; set; }
    }
}
