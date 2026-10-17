using System.Security.Cryptography;
using ExamAPI.Data;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Email
{
    /// <summary>
    /// Writes the body of a queued email whose content holds a secret (an OTP, a link token) at the moment it is
    /// sent, so the queue never stores one. Returns null when the email is no longer wanted (link revoked, ...).
    /// One implementation per <see cref="EmailQueue.Kind"/> other than Plain.
    /// </summary>
    public interface IEmailBodyRenderer
    {
        string Kind { get; }
        Task<EmailMessage?> RenderAsync(EmailQueue item, string? examOfficeEmail, CancellationToken ct);
    }

    public sealed record DailyEmailStatus(int Limit, int SentToday, int Queued)
    {
        public int Remaining => Math.Max(0, Limit - SentToday - Queued);
    }

    /// <summary>Queues email and answers the per-college daily count; OTPs are tried at once (<see cref="SendNowAsync"/>).</summary>
    public interface IEmailOutbox
    {
        /// <summary>Adds a queue row (saved with the caller's SaveChanges, so it commits with the links it belongs to).</summary>
        EmailQueue Enqueue(Guid collegeId, string kind, Guid? refId, string to, int priority, string? subject = null, string? html = null, string? text = null);

        /// <summary>
        /// Sends now (OTP). Recorded as Sent without its body. When there is no account or sending fails, it is queued
        /// at high priority instead (its renderer makes a fresh body then) and false is answered: "wait a minute".
        /// </summary>
        Task<bool> SendNowAsync(Guid collegeId, string kind, Guid? refId, EmailMessage message, CancellationToken ct = default);

        Task<DailyEmailStatus> DailyStatusAsync(Guid collegeId, CancellationToken ct = default);
    }

    public sealed class EmailOutbox : IEmailOutbox
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailAccountResolver _accounts;
        private readonly IEmailSender _sender;
        private readonly ILogger<EmailOutbox> _logger;

        public EmailOutbox(ApplicationDbContext context, IEmailAccountResolver accounts, IEmailSender sender, ILogger<EmailOutbox> logger)
        {
            _context = context;
            _accounts = accounts;
            _sender = sender;
            _logger = logger;
        }

        public EmailQueue Enqueue(Guid collegeId, string kind, Guid? refId, string to, int priority, string? subject = null, string? html = null, string? text = null)
        {
            var now = DateTime.UtcNow;
            var item = new EmailQueue
            {
                Id = Guid.NewGuid(),
                CollegeId = collegeId,
                Kind = kind,
                RefId = refId,
                ToAddress = to,
                Priority = priority,
                Subject = subject,
                HtmlBody = html,
                TextBody = text,
                Status = EmailStatuses.Pending,
                CreatedAt = now,
                NextAttemptAt = now,
            };
            _context.EmailQueue.Add(item);
            return item;
        }

        public async Task<bool> SendNowAsync(Guid collegeId, string kind, Guid? refId, EmailMessage message, CancellationToken ct = default)
        {
            string? error;
            try
            {
                var (account, _) = await _accounts.ForCollegeAsync(collegeId, ct);
                if (account == null)
                {
                    error = EmailQueueProcessor.NoAccount;
                }
                else
                {
                    await _sender.SendAsync(account, message, ct);
                    var sent = Enqueue(collegeId, kind, refId, message.To, EmailPriorities.High);
                    sent.Status = EmailStatuses.Sent;
                    sent.SentAt = DateTime.UtcNow;
                    sent.Attempts = 1;
                    await _context.SaveChangesAsync(ct);
                    return true;
                }
            }
            catch (CryptographicException)
            {
                error = EmailQueueProcessor.UnreadablePassword;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = EmailQueueProcessor.Clean(ex);
                _logger.LogWarning("Email: immediate {Kind} send failed for college {College}: {Error}", kind, collegeId, error);
            }

            var queued = Enqueue(collegeId, kind, refId, message.To, EmailPriorities.High);
            queued.Attempts = error == EmailQueueProcessor.NoAccount ? 0 : 1;
            queued.LastError = error;
            await _context.SaveChangesAsync(ct);
            return false;
        }

        public async Task<DailyEmailStatus> DailyStatusAsync(Guid collegeId, CancellationToken ct = default)
        {
            var since = EmailQueueProcessor.TodayStartUtc();
            var limit = await _context.CollegeSettings.IgnoreQueryFilters()
                .Where(s => s.CollegeId == collegeId).Select(s => (int?)s.DailyEmailLimit).FirstOrDefaultAsync(ct) ?? 2000;
            var sent = await _context.EmailQueue.IgnoreQueryFilters()
                .CountAsync(m => m.CollegeId == collegeId && m.SentAt >= since, ct);
            var queued = await _context.EmailQueue.IgnoreQueryFilters()
                .CountAsync(m => m.CollegeId == collegeId && m.Priority > EmailPriorities.High
                    && (m.Status == EmailStatuses.Pending || m.Status == EmailStatuses.Sending), ct);
            return new DailyEmailStatus(limit, sent, queued);
        }
    }

    /// <summary>
    /// Sends what is queued: OTPs first, then bulk mail oldest first, each college within its daily limit (bulk mail
    /// over the limit waits for tomorrow; OTPs always go). Rows are claimed with UPDLOCK/READPAST, so two copies of
    /// the API (an app pool recycle overlaps the old and the new process) never send the same email twice. A failed
    /// send is retried after 1, 5, 15 and 60 minutes, then marked Failed. Runs without a signed-in user, so every
    /// query ignores the tenant filter and names the college itself.
    /// </summary>
    public sealed class EmailQueueProcessor
    {
        public const string NoAccount = "No email account is set up for this college (or a platform default).";
        public const string UnreadablePassword = "The saved email password can't be read (the server key changed); enter it again on the platform page.";
        public const int MaxAttempts = 5;
        private static readonly TimeSpan[] Backoff = { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60) };
        private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan NoAccountWait = TimeSpan.FromMinutes(15);

        private readonly ApplicationDbContext _context;
        private readonly IEmailAccountResolver _accounts;
        private readonly IEmailSender _sender;
        private readonly Dictionary<string, IEmailBodyRenderer> _renderers;
        private readonly ILogger<EmailQueueProcessor> _logger;

        public EmailQueueProcessor(ApplicationDbContext context, IEmailAccountResolver accounts, IEmailSender sender,
            IEnumerable<IEmailBodyRenderer> renderers, ILogger<EmailQueueProcessor> logger)
        {
            _context = context;
            _accounts = accounts;
            _sender = sender;
            _renderers = renderers.ToDictionary(r => r.Kind);
            _logger = logger;
        }

        /// <summary>Midnight of the server's local day, in UTC (the daily limit resets then).</summary>
        public static DateTime TodayStartUtc() => DateTime.Today.ToUniversalTime();

        /// <summary>A send error for the queue row and the log: the exception message only, never a body or a password.</summary>
        public static string Clean(Exception ex)
        {
            var message = ex.Message.ReplaceLineEndings(" ").Trim();
            return message.Length > 1000 ? message[..1000] : message;
        }

        /// <summary>Claims and sends up to <paramref name="take"/> due emails; answers how many were claimed.</summary>
        public async Task<int> ProcessAsync(int take, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var ids = await ClaimAsync(take, now, ct);
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                await SendOneAsync(id, ct);
            }
            return ids.Count;
        }

        private async Task<List<Guid>> ClaimAsync(int take, DateTime now, CancellationToken ct)
        {
            var lockUntil = now + Lease;
            if (_context.Database.IsSqlServer())
            {
                // One statement: pick the next rows nobody else holds (READPAST) and mark them ours.
                return await _context.Database.SqlQuery<Guid>($@"
WITH next AS (
    SELECT TOP ({take}) * FROM EmailQueue WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE NextAttemptAt <= {now} AND (Status = 'Pending' OR (Status = 'Sending' AND LockedUntil < {now}))
    ORDER BY Priority, CreatedAt)
UPDATE next SET Status = 'Sending', LockedUntil = {lockUntil}
OUTPUT inserted.Id AS Value").ToListAsync(ct);
            }

            // Other providers (tests): the same choice without row locks.
            var due = await _context.EmailQueue.IgnoreQueryFilters()
                .Where(m => m.NextAttemptAt <= now && (m.Status == EmailStatuses.Pending || (m.Status == EmailStatuses.Sending && m.LockedUntil < now)))
                .OrderBy(m => m.Priority).ThenBy(m => m.CreatedAt)
                .Take(take)
                .ToListAsync(ct);
            foreach (var m in due)
            {
                m.Status = EmailStatuses.Sending;
                m.LockedUntil = lockUntil;
            }
            await _context.SaveChangesAsync(ct);
            return due.Select(m => m.Id).ToList();
        }

        private async Task SendOneAsync(Guid id, CancellationToken ct)
        {
            var item = await _context.EmailQueue.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id, ct);
            if (item == null || item.CollegeId == null) return;
            var collegeId = item.CollegeId.Value;
            var now = DateTime.UtcNow;
            var settings = await _context.CollegeSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.CollegeId == collegeId).Select(s => new { s.DailyEmailLimit, s.ExamOfficeEmail }).FirstOrDefaultAsync(ct);

            if (item.Priority > EmailPriorities.High)
            {
                var sentToday = await _context.EmailQueue.IgnoreQueryFilters()
                    .CountAsync(m => m.CollegeId == collegeId && m.SentAt >= TodayStartUtc(), ct);
                if (sentToday >= (settings?.DailyEmailLimit ?? 2000))
                {
                    await DeferAsync(item, DateTime.Today.AddDays(1).ToUniversalTime(), "Today's email limit is reached; it goes out tomorrow.", ct);
                    return;
                }
            }

            EmailAccountSettings? account;
            try
            {
                (account, _) = await _accounts.ForCollegeAsync(collegeId, ct);
            }
            catch (CryptographicException)
            {
                await DeferAsync(item, now + NoAccountWait, UnreadablePassword, ct);
                return;
            }
            if (account == null)
            {
                await DeferAsync(item, now + NoAccountWait, NoAccount, ct);
                return;
            }

            EmailMessage? message;
            if (item.Kind == EmailKinds.Plain)
            {
                message = new EmailMessage(item.ToAddress, item.Subject ?? "", item.HtmlBody ?? "", item.TextBody ?? "");
            }
            else if (_renderers.TryGetValue(item.Kind, out var renderer))
            {
                message = await renderer.RenderAsync(item, settings?.ExamOfficeEmail, ct);
                if (message == null)
                {
                    item.Status = EmailStatuses.Cancelled;
                    item.LockedUntil = null;
                    await _context.SaveChangesAsync(ct);
                    return;
                }
            }
            else
            {
                item.Status = EmailStatuses.Failed;
                item.LastError = $"Nothing can write a \"{item.Kind}\" email.";
                item.LockedUntil = null;
                await _context.SaveChangesAsync(ct);
                return;
            }

            try
            {
                await _sender.SendAsync(account, message, ct);
                item.Status = EmailStatuses.Sent;
                item.SentAt = DateTime.UtcNow;
                item.Attempts++;
                item.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                item.Attempts++;
                item.LastError = Clean(ex);
                if (item.Attempts >= MaxAttempts)
                {
                    item.Status = EmailStatuses.Failed;
                }
                else
                {
                    item.Status = EmailStatuses.Pending;
                    item.NextAttemptAt = DateTime.UtcNow + Backoff[Math.Min(item.Attempts, Backoff.Length) - 1];
                }
                _logger.LogWarning("Email: {Kind} {Id} to college {College} failed (attempt {Attempt}): {Error}", item.Kind, item.Id, collegeId, item.Attempts, item.LastError);
            }
            item.LockedUntil = null;
            await _context.SaveChangesAsync(ct);
        }

        private async Task DeferAsync(EmailQueue item, DateTime until, string reason, CancellationToken ct)
        {
            item.Status = EmailStatuses.Pending;
            item.NextAttemptAt = until;
            item.LastError = reason;
            item.LockedUntil = null;
            await _context.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Runs the <see cref="EmailQueueProcessor"/> every few seconds inside the API. The queue is in the database,
    /// so an app pool recycle loses nothing; keep the pool AlwaysRunning (DEPLOY_GUIDE) so it also runs at night.
    /// Settings: EmailQueue:Enabled (default true), EmailQueue:IntervalSeconds (default 15).
    /// </summary>
    public sealed class EmailQueueWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<EmailQueueWorker> _logger;
        private readonly bool _enabled;
        private readonly TimeSpan _interval;

        public EmailQueueWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<EmailQueueWorker> logger)
        {
            _scopes = scopes;
            _logger = logger;
            _enabled = config.GetValue("EmailQueue:Enabled", true);
            _interval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("EmailQueue:IntervalSeconds", 15), 5, 600));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("Email queue: disabled (EmailQueue:Enabled = false).");
                return;
            }
            const int batch = 50;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    int claimed;
                    do
                    {
                        using var scope = _scopes.CreateScope();
                        claimed = await scope.ServiceProvider.GetRequiredService<EmailQueueProcessor>().ProcessAsync(batch, stoppingToken);
                    } while (claimed == batch && !stoppingToken.IsCancellationRequested);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Email queue: a run failed; trying again shortly.");
                }
                try
                {
                    await Task.Delay(_interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
