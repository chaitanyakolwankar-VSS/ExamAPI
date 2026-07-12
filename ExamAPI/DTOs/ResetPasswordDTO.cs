namespace ExamAPI.DTOs
{
    public class ResetPasswordDTO
    {
        public Guid UserID { get; set; }
        public string Otp { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
