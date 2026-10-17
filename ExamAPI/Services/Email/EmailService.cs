using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ExamAPI.Services.Email
{
    /// <summary>
    /// Staff emails (password reset OTP): sent at once through the platform default account set on the platform
    /// page -- the same place as the student email accounts. Throws <see cref="InvalidOperationException"/> with
    /// what to do when no default account is set up.
    /// </summary>
    public partial class EmailService : IEmailService
    {
        private readonly IEmailAccountResolver _accounts;
        private readonly IEmailSender _sender;

        public EmailService(IEmailAccountResolver accounts, IEmailSender sender)
        {
            _accounts = accounts;
            _sender = sender;
        }

        [GeneratedRegex("<[^>]+>")]
        private static partial Regex Tags();

        [GeneratedRegex(@"[ \t]*\n\s*")]
        private static partial Regex BlankLines();

        [GeneratedRegex(@"[ \t]{2,}")]
        private static partial Regex Spaces();

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            EmailAccountSettings? account;
            try
            {
                account = await _accounts.DefaultAsync();
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException(EmailQueueProcessor.UnreadablePassword);
            }
            if (account == null)
                throw new InvalidOperationException("Email is not set up yet: the platform admin has to add the default email account.");

            var plain = WebUtility.HtmlDecode(Tags().Replace(body.Replace("<br>", "\n").Replace("</p>", "\n"), " "));
            var text = BlankLines().Replace(Spaces().Replace(plain, " "), "\n").Trim();
            var html = $"<!doctype html><html><body>{body}</body></html>";
            await _sender.SendAsync(account, new EmailMessage(toEmail, subject, html, text));
        }
    }
}
