using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.StudentAccess;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Email
{
    /// <summary>An account as the platform screen shows it: never the password, only whether one is saved.</summary>
    public class EmailAccountView
    {
        public bool Exists { get; set; }
        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public bool UseStartTls { get; set; } = true;
        public string Username { get; set; } = "";
        public bool HasPassword { get; set; }
        public string FromAddress { get; set; } = "";
        public string? FromName { get; set; }
        public DateTime? UpdatedAt { get; set; }
        /// <summary>For a college without its own account: whether the platform default will be used.</summary>
        public bool DefaultExists { get; set; }
    }

    public class EmailAccountInput
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public bool UseStartTls { get; set; } = true;
        public string? Username { get; set; }
        /// <summary>Blank keeps the saved password.</summary>
        public string? Password { get; set; }
        public string? FromAddress { get; set; }
        public string? FromName { get; set; }
    }

    public interface IEmailAccountService
    {
        /// <summary>collegeId null = the platform default.</summary>
        Task<EmailAccountView> GetAsync(Guid? collegeId);
        Task<EmailAccountView> SaveAsync(Guid? collegeId, EmailAccountInput input);
        Task DeleteAsync(Guid? collegeId);
        /// <summary>Sends a test email through the account the college would use; answers null when it went, else the error.</summary>
        Task<string?> SendTestAsync(Guid? collegeId, string to);
    }

    /// <summary>
    /// Platform admin only (the controller's policy): the mail accounts emails are sent from. A college's own
    /// account, else the platform default. Passwords are stored encrypted and never returned; every change is audited.
    /// </summary>
    public sealed class EmailAccountService : IEmailAccountService
    {
        private readonly ApplicationDbContext _context;
        private readonly ISecretProtector _protector;
        private readonly IEmailAccountResolver _resolver;
        private readonly IEmailSender _sender;

        public EmailAccountService(ApplicationDbContext context, ISecretProtector protector, IEmailAccountResolver resolver, IEmailSender sender)
        {
            _context = context;
            _protector = protector;
            _resolver = resolver;
            _sender = sender;
        }

        private Task<EmailAccount?> FindAsync(Guid? collegeId) =>
            _context.EmailAccounts.FirstOrDefaultAsync(a => collegeId == null ? a.CollegeId == null : a.CollegeId == collegeId);

        private async Task<string> CollegeNameAsync(Guid? collegeId)
        {
            if (collegeId == null) return "the platform default";
            var name = await _context.Colleges.IgnoreQueryFilters().Where(c => c.CollegeId == collegeId).Select(c => c.Name).FirstOrDefaultAsync();
            return name ?? throw new KeyNotFoundException("College not found.");
        }

        public async Task<EmailAccountView> GetAsync(Guid? collegeId)
        {
            await CollegeNameAsync(collegeId);
            var account = await FindAsync(collegeId);
            var defaultExists = collegeId == null ? account != null : await _context.EmailAccounts.AnyAsync(a => a.CollegeId == null);
            if (account == null) return new EmailAccountView { DefaultExists = defaultExists };
            return new EmailAccountView
            {
                Exists = true,
                Host = account.Host,
                Port = account.Port,
                UseStartTls = account.UseStartTls,
                Username = account.Username,
                HasPassword = !string.IsNullOrEmpty(account.PasswordProtected),
                FromAddress = account.FromAddress,
                FromName = account.FromName,
                UpdatedAt = account.UpdatedAt,
                DefaultExists = defaultExists,
            };
        }

        public async Task<EmailAccountView> SaveAsync(Guid? collegeId, EmailAccountInput input)
        {
            var name = await CollegeNameAsync(collegeId);
            var host = (input.Host ?? "").Trim();
            var username = (input.Username ?? "").Trim();
            var from = StudentEmail.Normalize(input.FromAddress);
            var password = (input.Password ?? "").Replace(" ", ""); // Google shows app passwords in groups of four
            if (host == "" || host.Any(char.IsWhiteSpace) || host.Length > 255)
                throw new InvalidOperationException("Enter the mail server, e.g. smtp.gmail.com.");
            if (input.Port is < 1 or > 65535)
                throw new InvalidOperationException("Enter the port, e.g. 587.");
            if (username == "" || username.Length > 255)
                throw new InvalidOperationException("Enter the account's sign-in (usually the Gmail address).");
            if (!StudentEmail.IsValid(from))
                throw new InvalidOperationException("Enter the address emails are sent from, e.g. noreply@college.edu.");
            if ((input.FromName ?? "").Trim().Length > 100)
                throw new InvalidOperationException("The sender name can be at most 100 characters.");

            var account = await FindAsync(collegeId);
            if (account == null && password == "")
                throw new InvalidOperationException("Enter the account's app password.");
            var isNew = account == null;
            if (account == null)
            {
                account = new EmailAccount { Id = Guid.NewGuid(), CollegeId = collegeId, Host = host, Username = username, FromAddress = from };
                _context.EmailAccounts.Add(account);
            }
            account.Host = host;
            account.Port = input.Port;
            account.UseStartTls = input.UseStartTls;
            account.Username = username;
            account.FromAddress = from;
            account.FromName = string.IsNullOrWhiteSpace(input.FromName) ? null : input.FromName.Trim();
            if (password != "") account.PasswordProtected = _protector.Protect(password);
            account.UpdatedAt = DateTime.UtcNow;
            account.UpdatedBy = _context.CurrentUserId;

            Audit(collegeId, account.Id, isNew ? "EmailAccountAdded" : "EmailAccountChanged",
                $"Sender for {name}: {username} via {host}:{input.Port}, from {from}; password {(password != "" ? "set" : "kept")}.");
            await _context.SaveChangesAsync();
            return await GetAsync(collegeId);
        }

        public async Task DeleteAsync(Guid? collegeId)
        {
            var name = await CollegeNameAsync(collegeId);
            var account = await FindAsync(collegeId);
            if (account == null) return;
            _context.EmailAccounts.Remove(account);
            Audit(collegeId, account.Id, "EmailAccountRemoved", $"Sender for {name} removed ({account.Username}).");
            await _context.SaveChangesAsync();
        }

        public async Task<string?> SendTestAsync(Guid? collegeId, string to)
        {
            var name = await CollegeNameAsync(collegeId);
            var address = StudentEmail.Normalize(to);
            if (!StudentEmail.IsValid(address))
                return "Enter a valid email address to send the test to.";
            try
            {
                var (account, source) = collegeId == null
                    ? (await _resolver.DefaultAsync(), EmailAccountSources.Default)
                    : await _resolver.ForCollegeAsync(collegeId);
                if (account == null)
                    return EmailQueueProcessor.NoAccount;
                var via = source == EmailAccountSources.Default && collegeId != null ? " (through the platform default account)" : "";
                var message = EmailLayout.Build(address, "GradeSphere test email",
                    $"<p>This is a test email for {System.Net.WebUtility.HtmlEncode(name)}{via}.</p><p>If you can read it, emails are set up correctly.</p>",
                    $"This is a test email for {name}{via}.\nIf you can read it, emails are set up correctly.", null);
                await _sender.SendAsync(account, message);
                return null;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return EmailQueueProcessor.UnreadablePassword;
            }
            catch (Exception ex)
            {
                return EmailQueueProcessor.Clean(ex);
            }
        }

        private void Audit(Guid? collegeId, Guid accountId, string evt, string details) =>
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CollegeId = collegeId,
                Action = "UPDATE",
                TableName = nameof(EmailAccount),
                RecordId = accountId.ToString(),
                PerformedBy = _context.CurrentUserId,
                UserType = "Staff",
                Event = evt,
                Details = details.Length > 1000 ? details[..1000] : details,
            });
    }
}
