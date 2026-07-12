using ExamAPI.Models;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace ExamAPI.Services.Email
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _emailSettings;
        public EmailService(IOptions<EmailSettings> options)
        {
            _emailSettings = options.Value;

            Console.WriteLine($"SMTP: {_emailSettings.SmtpServer}");
            Console.WriteLine($"Port: {_emailSettings.Port}");
            Console.WriteLine($"Email: {_emailSettings.SenderEmail}");
            Console.WriteLine($"Password empty? {string.IsNullOrEmpty(_emailSettings.Password)}");
        }
        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            using (var client = new SmtpClient(_emailSettings.SmtpServer, _emailSettings.Port))
            {
                client.Credentials = new NetworkCredential(_emailSettings.SenderEmail, _emailSettings.Password);
                client.EnableSsl = true;
                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_emailSettings.SenderEmail, "No Reply - Exam Portal"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                mailMessage.ReplyToList.Add(
          new MailAddress("no-reply@vivacollege.org", "Do Not Reply")
      );
                mailMessage.Headers.Add("X-Auto-Response-Suppress", "OOF, AutoReply, All");
                mailMessage.Headers.Add("Auto-Submitted", "auto-generated");
                mailMessage.Headers.Add("Precedence", "bulk");
                mailMessage.To.Add(toEmail);
                await client.SendMailAsync(mailMessage);
            }
        }
    }
}
