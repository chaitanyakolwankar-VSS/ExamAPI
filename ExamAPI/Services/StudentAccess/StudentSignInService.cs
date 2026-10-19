using System.Security.Claims;
using System.Security.Cryptography;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.StudentAccess
{
    public static class LinkOpenStates
    {
        public const string Ready = "ready";
        /// <summary>Stopped by staff or past its deadline.</summary>
        public const string Closed = "closed";
        /// <summary>Unknown token (mistyped, or replaced by a newer email).</summary>
        public const string Invalid = "invalid";
    }

    /// <summary>What the link page shows before sign-in: no personal data beyond a first name and a masked email.</summary>
    public class LinkInfo
    {
        public string State { get; set; } = LinkOpenStates.Invalid;
        public string Message { get; set; } = "";
        public string? College { get; set; }
        public string? TaskType { get; set; }
        public string? TaskName { get; set; }
        public string? FirstName { get; set; }
        public string? MaskedEmail { get; set; }
        public DateTime? Deadline { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? HelpEmail { get; set; }
    }

    public class CodeSent
    {
        /// <summary>False: the email is slow and was queued; it arrives in a minute or two.</summary>
        public bool SentNow { get; set; }
        public string MaskedEmail { get; set; } = "";
        public int ValidMinutes { get; set; }
        public string Message { get; set; } = "";
    }

    public class StudentSignedIn
    {
        public string SessionToken { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }

    public class StudentDetailsView
    {
        public string StudentId { get; set; } = "";
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? MotherName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Category { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Prn { get; set; }
        public string? Email { get; set; }
        public bool HasPhoto { get; set; }
        public bool HasSignature { get; set; }
    }

    public class StudentHome
    {
        public string College { get; set; } = "";
        public string TaskType { get; set; } = "";
        public string TaskName { get; set; } = "";
        public DateTime Deadline { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<string> EditableFields { get; set; } = new();
        public string? HelpEmail { get; set; }
        public StudentDetailsView Student { get; set; } = new();
    }

    public interface IStudentSignInService
    {
        Task<LinkInfo> OpenAsync(string? token);
        Task<CodeSent> SendCodeAsync(string? token);
        Task<StudentSignedIn> VerifyAsync(string? token, string? code);
        Task<StudentHome> HomeAsync(ClaimsPrincipal student);
        Task SignOutAsync(ClaimsPrincipal student);
    }

    /// <summary>
    /// The student's side of a link: open it (state only), get a 6-digit code at the email on record, check it, and
    /// get a session for that one task. Limits per link: 3 codes an hour (a minute apart), 5 tries per code, codes
    /// work 10 minutes. The token and the code are never stored, only their hashes. Errors are plain sentences
    /// (InvalidOperationException) for the page to show.
    /// </summary>
    public sealed class StudentSignInService : IStudentSignInService
    {
        public const int CodesPerHour = 3;
        public const int MaxTries = 5;
        public static readonly TimeSpan CodeGap = TimeSpan.FromMinutes(1);

        private const string NotValid = "This link isn't valid. It may have been replaced by a newer email: use the latest one from your college.";

        private readonly ApplicationDbContext _context;
        private readonly IStudentAccessSecrets _secrets;
        private readonly IEmailOutbox _outbox;

        public StudentSignInService(ApplicationDbContext context, IStudentAccessSecrets secrets, IEmailOutbox outbox)
        {
            _context = context;
            _secrets = secrets;
            _outbox = outbox;
        }

        public static string MaskEmail(string? email)
        {
            if (string.IsNullOrEmpty(email)) return "";
            var at = email.IndexOf('@');
            if (at <= 0) return "•••";
            var local = email[..at];
            var shown = local.Length <= 2 ? local[..1] : local[..2];
            return $"{shown}•••{email[at..]}";
        }

        private async Task<TaskLink?> FindAsync(string? token)
        {
            token = token?.Trim();
            if (string.IsNullOrEmpty(token) || token.Length > 100) return null;
            var hash = _secrets.Hash(token);
            return await _context.TaskLinks.IgnoreQueryFilters()
                .Include(l => l.Student).Include(l => l.TaskType).Include(l => l.Batch)
                .FirstOrDefaultAsync(l => l.TokenHash == hash);
        }

        private Task<string?> HelpEmailAsync(Guid? collegeId) =>
            _context.CollegeSettings.IgnoreQueryFilters().Where(s => s.CollegeId == collegeId).Select(s => s.ExamOfficeEmail).FirstOrDefaultAsync();

        private static bool StudentOk(StudentMaster? s) => s is { IsActive: true, IsDeleted: false };

        /// <summary>The link, when it still lets the student in; else a plain reason.</summary>
        private async Task<TaskLink> OpenLinkAsync(string? token)
        {
            var link = await FindAsync(token) ?? throw new InvalidOperationException(NotValid);
            if (!StudentAccessAuth.IsOpen(link, DateTime.UtcNow) || !StudentOk(link.Student))
                throw new InvalidOperationException("This link is closed. Contact your college office if you still need it.");
            if (link.Student!.Email == null)
                throw new InvalidOperationException("There's no email on your record. Contact your college office.");
            return link;
        }

        public async Task<LinkInfo> OpenAsync(string? token)
        {
            var link = await FindAsync(token);
            if (link == null)
                return new LinkInfo { State = LinkOpenStates.Invalid, Message = NotValid };

            var info = new LinkInfo
            {
                College = await StudentLinkEmails.CollegeNameAsync(_context, link.CollegeId),
                TaskType = link.TaskTypeCode,
                TaskName = link.TaskType?.Name,
                Deadline = link.ExpiresAt,
                UpdatedAt = link.UsedAt,
                HelpEmail = await HelpEmailAsync(link.CollegeId),
            };
            if (!StudentAccessAuth.IsOpen(link, DateTime.UtcNow) || !StudentOk(link.Student) || link.Student!.Email == null)
            {
                info.State = LinkOpenStates.Closed;
                info.Message = link.UsedAt != null
                    ? $"Your details were updated on {link.UsedAt.Value.ToLocalTime():d MMM yyyy}. This link is now closed."
                    : "This link is closed. Contact your college office if you still need it.";
                return info;
            }
            info.State = LinkOpenStates.Ready;
            info.FirstName = link.Student.FirstName;
            info.MaskedEmail = MaskEmail(link.Student.Email);
            info.Message = "We'll email you a 6-digit code to confirm it's you.";
            return info;
        }

        public async Task<CodeSent> SendCodeAsync(string? token)
        {
            var link = await OpenLinkAsync(token);
            var now = DateTime.UtcNow;
            var recent = await _context.OtpChallenges.IgnoreQueryFilters()
                .Where(o => o.TaskLinkId == link.Id && o.CreatedAt > now.AddHours(-1))
                .Select(o => o.CreatedAt).ToListAsync();
            if (recent.Count > 0 && now - recent.Max() < CodeGap)
                throw new InvalidOperationException("A code was sent a moment ago. Check your inbox (and spam), or wait a minute to ask again.");
            if (recent.Count >= CodesPerHour)
                throw new InvalidOperationException($"You've asked for {CodesPerHour} codes in the last hour. Use the newest one, or try again later.");

            var code = _secrets.NewCode();
            var challenge = new OtpChallenge
            {
                Id = Guid.NewGuid(),
                CollegeId = link.CollegeId,
                TaskLinkId = link.Id,
                CreatedAt = now,
                ExpiresAt = now + StudentLinkEmails.CodeLifetime,
            };
            challenge.CodeHash = _secrets.CodeHash(challenge.Id, code);
            _context.OtpChallenges.Add(challenge);
            await _context.SaveChangesAsync();

            var college = await StudentLinkEmails.CollegeNameAsync(_context, link.CollegeId);
            var message = StudentLinkEmails.CodeMessage(link.Student!.Email!, college, code, await HelpEmailAsync(link.CollegeId));
            var sentNow = await _outbox.SendNowAsync(link.CollegeId!.Value, EmailKinds.Otp, challenge.Id, message);
            var masked = MaskEmail(link.Student.Email);
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

        public async Task<StudentSignedIn> VerifyAsync(string? token, string? code)
        {
            var link = await OpenLinkAsync(token);
            var digits = new string((code ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length != 6)
                throw new InvalidOperationException("Enter the 6-digit code from the email.");

            var now = DateTime.UtcNow;
            var challenge = await _context.OtpChallenges.IgnoreQueryFilters()
                .Where(o => o.TaskLinkId == link.Id && o.UsedAt == null)
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
            var until = now + StudentAccessAuth.Idle;
            var session = new StudentSession
            {
                Id = Guid.NewGuid(),
                CollegeId = link.CollegeId,
                SessionHash = _secrets.Hash(raw),
                TaskLinkId = link.Id,
                StdMstId = link.StdMstId,
                TaskTypeCode = link.TaskTypeCode,
                CreatedAt = now,
                LastSeenAt = now,
                ExpiresAt = until < link.ExpiresAt ? until : link.ExpiresAt,
            };
            _context.StudentSessions.Add(session);
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CollegeId = link.CollegeId,
                Action = "UPDATE",
                TableName = nameof(StudentSession),
                RecordId = session.Id.ToString(),
                PerformedBy = link.StdMstId,
                UserType = StudentAccessAuth.RoleName,
                Event = "StudentSignedIn",
                Details = $"{link.Student!.StudentId} signed in for {link.TaskTypeCode} with a code sent to {MaskEmail(link.Student.Email)}.",
            });
            await _context.SaveChangesAsync();
            return new StudentSignedIn { SessionToken = raw, ExpiresAt = session.ExpiresAt };
        }

        /// <summary>Reads through the tenant filter: the session's college is the caller's (claims).</summary>
        public async Task<StudentHome> HomeAsync(ClaimsPrincipal student)
        {
            var linkId = StudentAccessAuth.Read(student, StudentAccessAuth.LinkClaim) ?? throw new UnauthorizedAccessException();
            var link = await _context.TaskLinks.Include(l => l.Student).Include(l => l.TaskType).Include(l => l.Batch)
                .FirstOrDefaultAsync(l => l.Id == linkId) ?? throw new UnauthorizedAccessException();
            var s = link.Student ?? throw new UnauthorizedAccessException();
            return new StudentHome
            {
                College = await StudentLinkEmails.CollegeNameAsync(_context, link.CollegeId),
                TaskType = link.TaskTypeCode,
                TaskName = link.TaskType?.Name ?? link.TaskTypeCode,
                Deadline = link.ExpiresAt,
                UpdatedAt = link.UsedAt,
                EditableFields = StudentFields.Parse(link.Batch?.EditableFields),
                HelpEmail = await HelpEmailAsync(link.CollegeId),
                Student = new StudentDetailsView
                {
                    StudentId = s.StudentId,
                    FirstName = s.FirstName,
                    MiddleName = s.MiddleName,
                    LastName = s.LastName,
                    MotherName = s.MotherName,
                    DateOfBirth = s.DateOfBirth,
                    Gender = s.Gender,
                    Category = s.Category,
                    Phone = s.Phone,
                    Address = s.Address,
                    Prn = s.StudentPRN,
                    Email = s.Email,
                    HasPhoto = !string.IsNullOrEmpty(s.PhotoUrl),
                    HasSignature = !string.IsNullOrEmpty(s.SignUrl),
                },
            };
        }

        public async Task SignOutAsync(ClaimsPrincipal student)
        {
            var sessionId = StudentAccessAuth.Read(student, StudentAccessAuth.SessionClaim);
            var session = await _context.StudentSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
            if (session is { EndedAt: null })
            {
                session.EndedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
    }
}
