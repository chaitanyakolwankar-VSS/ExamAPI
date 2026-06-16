namespace ExamAPI.DTOs
{
    public class VerifyOtpDTO
    {
        public Guid UserId { get; set; }
        public string Otp { get; set; }
    }
}
