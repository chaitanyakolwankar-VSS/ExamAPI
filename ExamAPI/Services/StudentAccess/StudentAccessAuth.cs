using System.Security.Claims;
using System.Text.Encodings.Web;
using ExamAPI.Data;
using ExamAPI.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamAPI.Services.StudentAccess
{
    /// <summary>
    /// The student sign-in: a scheme of its own, separate from the staff JWT. Staff endpoints authenticate with the
    /// JWT only, so a student session never reaches them; student endpoints name <see cref="Policy"/>, so a staff
    /// token never reaches those. The session id travels in the <see cref="Header"/> header (sessionStorage in the
    /// browser tab); only its hash is stored. Claims: CollegeId (the tenant filter), the student (NameIdentifier, so
    /// the audit log names them), role Student, the link, the session and the task.
    /// </summary>
    public static class StudentAccessAuth
    {
        public const string Scheme = "StudentSession";
        public const string Header = "X-Student-Session";
        public const string Policy = "Student";
        public const string RoleName = "Student";
        public const string LinkClaim = "TaskLinkId";
        public const string SessionClaim = "StudentSessionId";
        public const string TaskClaim = "TaskType";

        /// <summary>A session ends after this long without use, and never outlives its link.</summary>
        public static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

        /// <summary>The link still lets the student in.</summary>
        public static bool IsOpen(TaskLink link, DateTime now) => link.Status == TaskLinkStatuses.Active && link.ExpiresAt > now;

        public static void AddStudentPolicy(this AuthorizationOptions options) =>
            options.AddPolicy(Policy, p => p
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireRole(RoleName)
                .RequireClaim(LinkClaim));

        public static Guid? Read(ClaimsPrincipal user, string claim) =>
            Guid.TryParse(user.FindFirstValue(claim), out var id) ? id : null;
    }

    public sealed class StudentSessionHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private const string Ended = "Your session has ended. Open the link from your email again.";
        private readonly ApplicationDbContext _context;
        private readonly IStudentAccessSecrets _secrets;

        public StudentSessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
            ApplicationDbContext context, IStudentAccessSecrets secrets)
            : base(options, logger, encoder)
        {
            _context = context;
            _secrets = secrets;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var raw = Request.Headers[StudentAccessAuth.Header].ToString();
            if (raw.Length == 0) return AuthenticateResult.NoResult();
            if (raw.Length > 100) return AuthenticateResult.Fail(Ended);

            var hash = _secrets.Hash(raw);
            var now = DateTime.UtcNow;
            var session = await _context.StudentSessions.IgnoreQueryFilters()
                .Include(s => s.TaskLink)
                .FirstOrDefaultAsync(s => s.SessionHash == hash);
            if (session?.TaskLink == null || session.EndedAt != null || session.ExpiresAt <= now || !StudentAccessAuth.IsOpen(session.TaskLink, now))
                return AuthenticateResult.Fail(Ended);

            // Sliding expiry, written at most once a minute.
            if (now - session.LastSeenAt > TimeSpan.FromMinutes(1))
            {
                session.LastSeenAt = now;
                var until = now + StudentAccessAuth.Idle;
                session.ExpiresAt = until < session.TaskLink.ExpiresAt ? until : session.TaskLink.ExpiresAt;
                await _context.SaveChangesAsync();
            }

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, session.StdMstId.ToString()),
                new Claim(ClaimTypes.Role, StudentAccessAuth.RoleName),
                new Claim("CollegeId", session.CollegeId?.ToString() ?? ""),
                new Claim(StudentAccessAuth.LinkClaim, session.TaskLinkId.ToString()),
                new Claim(StudentAccessAuth.SessionClaim, session.Id.ToString()),
                new Claim(StudentAccessAuth.TaskClaim, session.TaskTypeCode),
            }, StudentAccessAuth.Scheme);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), StudentAccessAuth.Scheme));
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Response.WriteAsJsonAsync(new { message = Ended });
        }
    }
}
