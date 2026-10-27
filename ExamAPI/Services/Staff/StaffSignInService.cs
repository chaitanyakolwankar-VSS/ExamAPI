using System.Security.Claims;
using System.Security.Cryptography;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using ExamAPI.Services.StudentAccess;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Staff
{
    public class StaffSignedIn
    {
        public string SessionToken { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }

    public interface IStaffSignInService
    {
        Task<LinkInfo> OpenAsync(string? token);
        Task<CodeSent> SendCodeAsync(string? token);
        Task<StaffSignedIn> VerifyAsync(string? token, string? code);
        Task SignOutAsync(ClaimsPrincipal staff);
    }

    /// <summary>
    /// The staff member's side of a link, the twin of <see cref="StudentSignInService"/>: open it (state only), get a
    /// 6-digit code at the email on record, check it, and get a session for that one link. Limits per link: 3 codes an
    /// hour (a minute apart), 5 tries per code, codes work 10 minutes. The token and the code are never stored, only
    /// their hashes. Errors are plain sentences (InvalidOperationException) for the page to show. The response shapes
    /// are the student ones (<see cref="LinkInfo"/>, <see cref="CodeSent"/>) so the page can be copied.
    /// </summary>
    public sealed class StaffSignInService : IStaffSignInService
    {
        public const string TaskType = "STAFF_LETTERS";
        public const int CodesPerHour = StudentSignInService.CodesPerHour;
        public const int MaxTries = StudentSignInService.MaxTries;
        public static readonly TimeSpan CodeGap = StudentSignInService.CodeGap;

        private const string NotValid = "This link isn't valid. It may have been replaced by a newer email: use the latest one from your college.";

        private readonly ApplicationDbContext _context;
        private readonly IStudentAccessSecrets _secrets;
        private readonly IEmailOutbox _outbox;

        public StaffSignInService(ApplicationDbContext context, IStudentAccessSecrets secrets, IEmailOutbox outbox)
        {
            _context = context;
            _secrets = secrets;
            _outbox = outbox;
        }

        private async Task<StaffLink?> FindAsync(string? token)
        {
            token = token?.Trim();
            if (string.IsNullOrEmpty(token) || token.Length > 100) return null;
            var hash = _secrets.Hash(token);
            return await _context.StaffLinks.IgnoreQueryFilters().Include(l => l.Staff).FirstOrDefaultAsync(l => l.TokenHash == hash);
        }

        private Task<string?> HelpEmailAsync(Guid? collegeId) =>
            _context.CollegeSettings.IgnoreQueryFilters().Where(s => s.CollegeId == collegeId).Select(s => s.ExamOfficeEmail).FirstOrDefaultAsync();

        /// <summary>"{Name} {ExamType}" of the link's exam, as the letters print it.</summary>
        private async Task<string> ExamTitleAsync(StaffLink link)
        {
            var exam = await _context.Exams.IgnoreQueryFilters().Where(e => e.ExamId == link.ExamId)
                .Select(e => new { e.Name, e.ExamType }).FirstOrDefaultAsync();
            return exam == null ? "your exam" : string.Join(' ', new[] { exam.Name, exam.ExamType }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        }

        /// <summary>The link, when it still lets the person in; else a plain reason.</summary>
        private async Task<StaffLink> OpenLinkAsync(string? token)
        {
            var link = await FindAsync(token) ?? throw new InvalidOperationException(NotValid);
            if (!StaffAccessAuth.IsOpen(link, DateTime.UtcNow) || !StaffAccessAuth.StaffOk(link.Staff))
                throw new InvalidOperationException("This link is closed. Contact your college office if you still need it.");
            return link;
        }

        public async Task<LinkInfo> OpenAsync(string? token)
        {
            var link = await FindAsync(token);
            if (link == null)
                return new LinkInfo { State = LinkOpenStates.Invalid, Message = NotValid };

            var examTitle = await ExamTitleAsync(link);
            var info = new LinkInfo
            {
                College = await StudentLinkEmails.CollegeNameAsync(_context, link.CollegeId),
                TaskType = TaskType,
                TaskName = $"Your letters for {examTitle}",
                Deadline = link.ExpiresAt,
                HelpEmail = await HelpEmailAsync(link.CollegeId),
            };
            if (!StaffAccessAuth.IsOpen(link, DateTime.UtcNow) || !StaffAccessAuth.StaffOk(link.Staff))
            {
                info.State = LinkOpenStates.Closed;
                info.Message = "This link is closed. Contact your college office if you still need it.";
                return info;
            }
            info.State = LinkOpenStates.Ready;
            info.FirstName = link.Staff!.FirstName;
            info.MaskedEmail = StudentSignInService.MaskEmail(link.Staff.Email);
            info.Message = "We'll email you a 6-digit code to confirm it's you.";
            return info;
        }

        public async Task<CodeSent> SendCodeAsync(string? token)
        {
            var link = await OpenLinkAsync(token);
            var now = DateTime.UtcNow;
            var recent = await _context.StaffOtps.IgnoreQueryFilters()
                .Where(o => o.LinkId == link.Id && o.CreatedAt > now.AddHours(-1))
                .Select(o => o.CreatedAt).ToListAsync();
            if (recent.Count > 0 && now - recent.Max() < CodeGap)
                throw new InvalidOperationException("A code was sent a moment ago. Check your inbox (and spam), or wait a minute to ask again.");
            if (recent.Count >= CodesPerHour)
                throw new InvalidOperationException($"You've asked for {CodesPerHour} codes in the last hour. Use the newest one, or try again later.");

            var code = _secrets.NewCode();
            var challenge = new StaffOtp
            {
                Id = Guid.NewGuid(),
                CollegeId = link.CollegeId,
                LinkId = link.Id,
                CreatedAt = now,
                ExpiresAt = now + StudentLinkEmails.CodeLifetime,
            };
            challenge.CodeHash = _secrets.CodeHash(challenge.Id, code);
            _context.StaffOtps.Add(challenge);
            await _context.SaveChangesAsync();

            var college = await StudentLinkEmails.CollegeNameAsync(_context, link.CollegeId);
            var message = StaffLinkEmails.CodeMessage(link.Staff!.Email, college, code, await HelpEmailAsync(link.CollegeId));
            var sentNow = await _outbox.SendNowAsync(link.CollegeId!.Value, StaffEmailKinds.Otp, challenge.Id, message);
            var masked = StudentSignInService.MaskEmail(link.Staff.Email);
            return new CodeSent
            {
                SentNow = sentNow,
                MaskedEmail = masked,
                ValidMinutes = (int)StudentLinkEmails.CodeLifetime.TotalMinutes,
                Message = sentNow
                    ? $"We've sent a code to {masked}."
                    : $"Email is slow right now: your code to {masked} should arrive in a minute or two.",
            };
        }

        public async Task<StaffSignedIn> VerifyAsync(string? token, string? code)
        {
            var link = await OpenLinkAsync(token);
            var digits = new string((code ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length != 6)
                throw new InvalidOperationException("Enter the 6-digit code from the email.");

            var now = DateTime.UtcNow;
            var challenge = await _context.StaffOtps.IgnoreQueryFilters()
                .Where(o => o.LinkId == link.Id && o.UsedAt == null)
                .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync();
            if (challenge?.CodeHash == null)
                throw new InvalidOperationException("Ask for a code first.");
            if (challenge.ExpiresAt <= now)
                throw new InvalidOperationException("This code has expired. Ask for a new one.");
            if (challenge.AttemptCount >= MaxTries)
                throw new InvalidOperationException("Too many wrong tries. Ask for a new code.");

            if (!CryptographicOperations.FixedTimeEquals(challenge.CodeHash, _secrets.CodeHash(challenge.Id, digits)))
            {
                challenge.AttemptCount++;
                await _context.SaveChangesAsync();
                var left = MaxTries - challenge.AttemptCount;
                throw new InvalidOperationException(left > 0
                    ? $"That code isn't right. {left} {(left == 1 ? "try" : "tries")} left."
                    : "That code isn't right. Ask for a new code.");
            }

            challenge.UsedAt = now;
            link.OpenedAt ??= now;
            var raw = _secrets.NewToken();
            var until = now + StaffAccessAuth.Idle;
            var session = new StaffSession
            {
                Id = Guid.NewGuid(),
                CollegeId = link.CollegeId,
                SessionHash = _secrets.Hash(raw),
                LinkId = link.Id,
                StaffId = link.StaffId,
                CreatedAt = now,
                LastSeenAt = now,
                ExpiresAt = until < link.ExpiresAt ? until : link.ExpiresAt,
            };
            _context.StaffSessions.Add(session);
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CollegeId = link.CollegeId,
                Action = "UPDATE",
                TableName = nameof(StaffSession),
                RecordId = session.Id.ToString(),
                PerformedBy = link.StaffId,
                UserType = StaffAccessAuth.RoleName,
                Event = "StaffSignedIn",
                Details = $"{link.Staff!.EmployeeCode ?? StaffService.Letter(link.Staff.Title, link.Staff.FirstName, link.Staff.MiddleName, link.Staff.LastName)} signed in to the letters link with a code sent to {StudentSignInService.MaskEmail(link.Staff.Email)}.",
            });
            await _context.SaveChangesAsync();
            return new StaffSignedIn { SessionToken = raw, ExpiresAt = session.ExpiresAt };
        }

        /// <summary>Reads through the tenant filter: the session's college is the caller's (claims).</summary>
        public async Task SignOutAsync(ClaimsPrincipal staff)
        {
            var sessionId = StaffAccessAuth.Read(staff, StaffAccessAuth.SessionClaim);
            var session = await _context.StaffSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
            if (session is { EndedAt: null })
            {
                session.EndedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
    }
}
