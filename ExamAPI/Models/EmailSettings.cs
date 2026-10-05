namespace ExamAPI.Models
{
    public class EmailSettings
    {

        public string SenderEmail { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string SmtpServer { get; set; } = string.Empty;
        public int Port { get; set; }
        /// <summary>Display name on the reset mails; default "GradeSphere".</summary>
        public string SenderName { get; set; } = string.Empty;
    }
}
