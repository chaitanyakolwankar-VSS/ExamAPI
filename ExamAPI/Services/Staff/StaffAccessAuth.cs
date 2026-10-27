using System.Security.Claims;
using System.Text.Encodings.Web;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.StudentAccess;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamAPI.Services.Staff
{
    /// <summary>
    /// The staff member's letters sign-in: a scheme of its own, separate from both the staff JWT and the student session.
    /// Office endpoints authenticate with the JWT only, so this session never reaches them; the staff-site endpoints
    /// name <see cref="Policy"/>, so no JWT and no student session reaches those. The session id travels in the
    /// <see cref="Header"/> header (sessionStorage in the browser tab); only its hash is stored. Claims: CollegeId (the
    /// tenant filter), the staff member (NameIdentifier, so the audit log names them), role <see cref="RoleName"/>, the
    /// link and the session.
    /// </summary>
    public static class StaffAccessAuth
    {
        public const string Scheme = "StaffSession";
        public const string Header = "X-Staff-Session";
        public const string Policy = "StaffMemberSite";
        public const string RoleName = "StaffMember";
        public const string LinkClaim = "StaffLinkId";
        public const string SessionClaim = "StaffSessionId";

        /// <summary>A session ends after this long without use, and never outlives its link.</summary>
        public static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

        /// <summary>The link still lets the person in.</summary>
        public static bool IsOpen(StaffLink link, DateTime now) => link.Status == StaffLinkStatuses.Active && link.ExpiresAt > now;

        /// <summary>The person is still on the college's staff list (not marked as left, not deleted).</summary>
        public static bool StaffOk(StaffMaster? staff) => staff is { IsActive: true, IsDeleted: false };

        public static void AddStaffMemberPolicy(this AuthorizationOptions options) =>
            options.AddPolicy(Policy, p => p
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireRole(RoleName)
                .RequireClaim(LinkClaim));

        public static Guid? Read(ClaimsPrincipal user, string claim) =>
            Guid.TryParse(user.FindFirstValue(claim), out var id) ? id : null;
    }

    public sealed class StaffSessionHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private const string Ended = "Your session has ended. Open the link from your email again.";
        private readonly ApplicationDbContext _context;
        private readonly IStudentAccessSecrets _secrets;

        public StaffSessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
            ApplicationDbContext context, IStudentAccessSecrets secrets)
            : base(options, logger, encoder)
        {
            _context = context;
            _secrets = secrets;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var raw = Request.Headers[StaffAccessAuth.Header].ToString();
            if (raw.Length == 0) return AuthenticateResult.NoResult();
            if (raw.Length > 100) return AuthenticateResult.Fail(Ended);

            var hash = _secrets.Hash(raw);
            var now = DateTime.UtcNow;
            var session = await _context.StaffSessions.IgnoreQueryFilters()
                .Include(s => s.Link).ThenInclude(l => l!.Staff)
                .FirstOrDefaultAsync(s => s.SessionHash == hash);
            // The link is checked on every request: stopping it (or sending a newer one) ends the session at once.
            if (session?.Link == null || session.EndedAt != null || session.ExpiresAt <= now
                || !StaffAccessAuth.IsOpen(session.Link, now) || !StaffAccessAuth.StaffOk(session.Link.Staff))
                return AuthenticateResult.Fail(Ended);

            // Sliding expiry, written at most once a minute.
            if (now - session.LastSeenAt > TimeSpan.FromMinutes(1))
            {
                session.LastSeenAt = now;
                var until = now + StaffAccessAuth.Idle;
                session.ExpiresAt = until < session.Link.ExpiresAt ? until : session.Link.ExpiresAt;
                await _context.SaveChangesAsync();
            }

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, session.StaffId.ToString()),
                new Claim(ClaimTypes.Role, StaffAccessAuth.RoleName),
                new Claim("CollegeId", session.CollegeId?.ToString() ?? ""),
                new Claim(StaffAccessAuth.LinkClaim, session.LinkId.ToString()),
                new Claim(StaffAccessAuth.SessionClaim, session.Id.ToString()),
            }, StaffAccessAuth.Scheme);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), StaffAccessAuth.Scheme));
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Response.WriteAsJsonAsync(new { message = Ended });
        }
    }
}
