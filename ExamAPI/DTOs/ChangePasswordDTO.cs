namespace ExamAPI.DTOs
{
    public class ChangePasswordDTO
    {
        public Guid UserId { get; set; }
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
    }
}
