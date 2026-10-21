using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.StudentAccess;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ExamAPI.Tests;

/// <summary>
/// The real API (Program.cs: real routing, real authentication schemes, real authorization policies, real rate
/// limiter) hosted in memory on an EF in-memory database, with two colleges seeded for the access audit. Nothing is
/// sent by mail (the hosted services are removed) and no real database is touched.
/// </summary>
public sealed class AccessAuditFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "access-audit-test-signing-key-0123456789-abcdefghijklmnop";
    public const string Issuer = "test-issuer";
    public const string Audience = "test-aud";

    private static readonly Dictionary<string, string> Settings = new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Server=unused;Database=unused",
        ["Jwt:Key"] = JwtKey,
        ["Jwt:Issuer"] = Issuer,
        ["Jwt:Audience"] = Audience,
        ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
    };

    static AccessAuditFactory()
    {
        // Program.cs reads these while it is still building the host, before test configuration is merged in, so
        // they also go in as environment variables (same values for every factory; they override appsettings.json).
        foreach (var (key, value) in Settings)
            Environment.SetEnvironmentVariable(key.Replace(":", "__"), value);
    }

    private readonly string _database = "access-audit-" + Guid.NewGuid();
    private readonly object _seedLock = new();
    private bool _seeded;
    private readonly StudentAccessSecrets _secrets = new(JwtKey);

    public Guid CollegeA { get; } = Guid.NewGuid();
    public Guid CollegeB { get; } = Guid.NewGuid();
    public Guid StudentSA1 { get; } = Guid.NewGuid();
    public Guid StudentSA2 { get; } = Guid.NewGuid();
    public Guid StudentSB1 { get; } = Guid.NewGuid();
    public Guid BatchA { get; } = Guid.NewGuid();
    public Guid BatchB { get; } = Guid.NewGuid();
    public Guid LinkA { get; } = Guid.NewGuid();
    public Guid LinkB { get; } = Guid.NewGuid();
    /// <summary>A second link of SA1 used only by the audit loops (the requests may sign out or use it).</summary>
    public Guid LinkAudit { get; } = Guid.NewGuid();
    public Guid LinkRevoked { get; } = Guid.NewGuid();

    public const string StudentIdSA1 = "A-0001";
    public const string StudentIdSA2 = "A-0002";
    public const string StudentIdSB1 = "B-0001";

    /// <summary>Raw session ids (the header values); only their hashes are stored.</summary>
    public string SessionA { get; private set; } = "";
    public string SessionB { get; private set; } = "";
    public string SessionEnded { get; private set; } = "";
    public string SessionRevokedLink { get; private set; } = "";

    public string StaffTokenA { get; private set; } = "";
    public string StaffTokenB { get; private set; } = "";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production"); // unhandled errors become a 500 answer instead of a thrown exception
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            // EF Core 9 keeps the provider choice in several registrations; remove them all, then use the in-memory one.
            var stale = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                || d.ServiceType == typeof(DbContextOptions)
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")
                    && d.ServiceType.GenericTypeArguments.Contains(typeof(ApplicationDbContext)))).ToList();
            foreach (var descriptor in stale) services.Remove(descriptor);
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(_database));

            // Nothing may run in the background (mail sender, keep-alive).
            var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var descriptor in hosted) services.Remove(descriptor);
        });
    }

    /// <summary>Seeds once (the first call builds the host).</summary>
    public void EnsureSeeded()
    {
        lock (_seedLock)
        {
            if (_seeded) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
            // The model's reference rows (task types) are only written when EnsureCreated is first to touch the store; the
            // startup seeder in Program.cs got there first, so add the ones the audit needs.
            if (!db.TaskTypes.Any())
            {
                db.TaskTypes.Add(new TaskType { Code = TaskTypes.StudentMaster, Name = "Verify student details", ExpiryRule = TaskExpiryRules.OnDeadline, RequiresOtp = true });
                db.TaskTypes.Add(new TaskType { Code = TaskTypes.HallTicket, Name = "Download hall ticket", ExpiryRule = TaskExpiryRules.OnDeadline, RequiresOtp = true });
                db.SaveChanges();
            }

            var ay = Guid.NewGuid();
            db.Colleges.Add(new College { CollegeId = CollegeA, Name = "College A", CollegeCode = "CA", CollegeCenter = "Virar", ContactEmail = "a@a.in", ContactPhone = "1" });
            db.Colleges.Add(new College { CollegeId = CollegeB, Name = "College B", CollegeCode = "CB", CollegeCenter = "Vasai", ContactEmail = "b@b.in", ContactPhone = "2" });
            var courseA = Guid.NewGuid();
            var courseB = Guid.NewGuid();
            db.CourseMasters.Add(new CourseMaster { CourseId = courseA, Name = "Civil", CourseCode = "CIV", CollegeId = CollegeA });
            db.CourseMasters.Add(new CourseMaster { CourseId = courseB, Name = "Civil", CourseCode = "CIV", CollegeId = CollegeB });

            db.StudentMasters.Add(Student(StudentSA1, StudentIdSA1, "Asha", "asha@a.in", CollegeA));
            db.StudentMasters.Add(Student(StudentSA2, StudentIdSA2, "Ravi", "ravi@a.in", CollegeA));
            db.StudentMasters.Add(Student(StudentSB1, StudentIdSB1, "Neha", "neha@b.in", CollegeB));

            var future = DateTime.UtcNow.AddDays(7);
            db.TaskBatches.Add(Batch(BatchA, CollegeA, courseA, ay, future));
            db.TaskBatches.Add(Batch(BatchB, CollegeB, courseB, ay, future));
            db.TaskLinks.Add(Link(LinkA, CollegeA, BatchA, StudentSA1, future));
            db.TaskLinks.Add(Link(LinkAudit, CollegeA, BatchA, StudentSA1, future));
            db.TaskLinks.Add(Link(LinkB, CollegeB, BatchB, StudentSB1, future));
            var revoked = Link(LinkRevoked, CollegeA, BatchA, StudentSA2, future);
            revoked.Status = TaskLinkStatuses.Revoked;
            revoked.RevokedAt = DateTime.UtcNow;
            db.TaskLinks.Add(revoked);

            SessionA = NewToken();
            SessionB = NewToken();
            SessionEnded = NewToken();
            SessionRevokedLink = NewToken();
            db.StudentSessions.Add(Session(SessionA, CollegeA, LinkA, StudentSA1));
            db.StudentSessions.Add(Session(SessionB, CollegeB, LinkB, StudentSB1));
            var ended = Session(SessionEnded, CollegeA, LinkA, StudentSA1);
            ended.EndedAt = DateTime.UtcNow;
            db.StudentSessions.Add(ended);
            db.StudentSessions.Add(Session(SessionRevokedLink, CollegeA, LinkRevoked, StudentSA2));
            db.SaveChanges();

            StaffTokenA = StaffToken(CollegeA);
            StaffTokenB = StaffToken(CollegeB);
            _seeded = true;
        }
    }

    /// <summary>
    /// A new, valid session of SA1 on the audit link (which is put back to Active first), for requests that may
    /// sign the session out or use the link up.
    /// </summary>
    public string NewAuditSession()
    {
        EnsureSeeded();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var link = db.TaskLinks.IgnoreQueryFilters().Single(l => l.Id == LinkAudit);
        link.Status = TaskLinkStatuses.Active;
        link.UsedAt = null;
        link.ExpiresAt = DateTime.UtcNow.AddDays(7);
        var token = NewToken();
        db.StudentSessions.Add(Session(token, CollegeA, LinkAudit, StudentSA1));
        db.SaveChanges();
        return token;
    }

    public string LinkStatus(Guid linkId)
    {
        EnsureSeeded();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.TaskLinks.IgnoreQueryFilters().Single(l => l.Id == linkId).Status;
    }

    private string NewToken() => _secrets.NewToken();

    private StudentSession Session(string raw, Guid college, Guid link, Guid student) => new()
    {
        Id = Guid.NewGuid(),
        CollegeId = college,
        SessionHash = _secrets.Hash(raw),
        TaskLinkId = link,
        StdMstId = student,
        TaskTypeCode = TaskTypes.StudentMaster,
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddMinutes(30),
    };

    private static StudentMaster Student(Guid id, string studentId, string first, string email, Guid college) => new()
    {
        StdMstId = id, StudentId = studentId, FirstName = first, LastName = "Patil", Email = email, IsActive = true, CollegeId = college,
    };

    private static TaskBatch Batch(Guid id, Guid college, Guid course, Guid ay, DateTime deadline) => new()
    {
        Id = id, CollegeId = college, TaskTypeCode = TaskTypes.StudentMaster, Title = "Verify details", Deadline = deadline,
        CourseId = course, SemesterId = "Sem-1", AYID = ay, EditableFields = "firstName,phone",
    };

    private static TaskLink Link(Guid id, Guid college, Guid batch, Guid student, DateTime expires) => new()
    {
        Id = id, CollegeId = college, BatchId = batch, StdMstId = student, TaskTypeCode = TaskTypes.StudentMaster,
        Status = TaskLinkStatuses.Active, ExpiresAt = expires,
    };

    /// <summary>A staff token as AuthService makes it: user id, name, role and the college.</summary>
    private static string StaffToken(Guid college)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, "teacher@" + college.ToString("N")[..6]),
            new(ClaimTypes.Role, "Teacher"),
            new("CollegeId", college.ToString()),
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)), SecurityAlgorithms.HmacSha256Signature),
        }));
    }
}
