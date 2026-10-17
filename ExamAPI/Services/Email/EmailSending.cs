using System.Net;
using ExamAPI.Data;
using ExamAPI.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace ExamAPI.Services.Email
{
    /// <summary>One email: an HTML and a plain-text body of the same content.</summary>
    public sealed record EmailMessage(string To, string Subject, string Html, string Text);

    /// <summary>A sending account with its password decrypted, for one send only (never logged or returned).</summary>
    public sealed record EmailAccountSettings(string Provider, string Host, int Port, bool UseStartTls, string Username, string Password, string FromAddress, string? FromName);

    /// <summary>Sends one message through an account. SMTP (MailKit) today; an HTTPS mail API can be another implementation.</summary>
    public interface IEmailSender
    {
        Task SendAsync(EmailAccountSettings account, EmailMessage message, CancellationToken ct = default);
    }

    public sealed class SmtpEmailSender : IEmailSender
    {
        public async Task SendAsync(EmailAccountSettings account, EmailMessage message, CancellationToken ct = default)
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(account.FromName) ? "GradeSphere" : account.FromName, account.FromAddress));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Headers.Add("Auto-Submitted", "auto-generated");
            mime.Headers.Add("X-Auto-Response-Suppress", "All");
            mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();

            using var client = new SmtpClient { Timeout = 30_000 };
            var security = account.Port == 465 ? SecureSocketOptions.SslOnConnect
                : account.UseStartTls ? SecureSocketOptions.StartTls
                : SecureSocketOptions.Auto;
            await client.ConnectAsync(account.Host, account.Port, security, ct);
            if (!string.IsNullOrEmpty(account.Username))
                await client.AuthenticateAsync(account.Username, account.Password, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
        }
    }

    /// <summary>
    /// Every student email has the same frame: the content, then "This is an automated message. For help, contact
    /// &lt;exam office email&gt;." Plain HTML and text; no images, no tracking.
    /// </summary>
    public static class EmailLayout
    {
        public static EmailMessage Build(string to, string subject, string htmlContent, string textContent, string? examOfficeEmail)
        {
            var help = string.IsNullOrWhiteSpace(examOfficeEmail) ? "your college's exam office" : examOfficeEmail;
            var helpHtml = string.IsNullOrWhiteSpace(examOfficeEmail)
                ? WebUtility.HtmlEncode(help)
                : $"<a href=\"mailto:{WebUtility.HtmlEncode(examOfficeEmail)}\">{WebUtility.HtmlEncode(examOfficeEmail)}</a>";
            var html = $"<!doctype html><html><body style=\"font-family:Arial,sans-serif;font-size:14px;color:#1f2937;line-height:1.5\">"
                + htmlContent
                + $"<hr style=\"border:none;border-top:1px solid #e5e7eb;margin:24px 0 12px\"><p style=\"font-size:12px;color:#6b7280\">This is an automated message. For help, contact {helpHtml}.</p>"
                + "</body></html>";
            var text = textContent.TrimEnd() + $"\n\n--\nThis is an automated message. For help, contact {help}.\n";
            return new EmailMessage(to, subject, html, text);
        }
    }

    /// <summary>Which account a college's emails go out from: its own, else the platform default, else none.</summary>
    public interface IEmailAccountResolver
    {
        Task<(EmailAccountSettings? Account, string Source)> ForCollegeAsync(Guid? collegeId, CancellationToken ct = default);
    }

    public static class EmailAccountSources
    {
        public const string College = "College";
        public const string Default = "Default";
        public const string None = "None";
    }

    public sealed class EmailAccountResolver : IEmailAccountResolver
    {
        private readonly ApplicationDbContext _context;
        private readonly ISecretProtector _protector;

        public EmailAccountResolver(ApplicationDbContext context, ISecretProtector protector)
        {
            _context = context;
            _protector = protector;
        }

        public async Task<(EmailAccountSettings? Account, string Source)> ForCollegeAsync(Guid? collegeId, CancellationToken ct = default)
        {
            var accounts = await _context.EmailAccounts.AsNoTracking()
                .Where(a => a.CollegeId == null || (collegeId != null && a.CollegeId == collegeId))
                .ToListAsync(ct);
            var own = collegeId == null ? null : accounts.FirstOrDefault(a => a.CollegeId == collegeId);
            var chosen = own ?? accounts.FirstOrDefault(a => a.CollegeId == null);
            if (chosen == null) return (null, EmailAccountSources.None);
            var password = string.IsNullOrEmpty(chosen.PasswordProtected) ? "" : _protector.Unprotect(chosen.PasswordProtected);
            return (new EmailAccountSettings(chosen.Provider, chosen.Host, chosen.Port, chosen.UseStartTls, chosen.Username, password, chosen.FromAddress, chosen.FromName),
                own != null ? EmailAccountSources.College : EmailAccountSources.Default);
        }
    }
}
