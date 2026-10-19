using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using ExamAPI.Services.StudentAccess;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>
/// Student access, step 5: staff send a class its links, the queued email gets a token when it is written, the
/// student opens the link, gets a 6-digit code and signs in to a session for that one task; staff stop, extend,
/// remind and resend. Limits on codes and tries; the student session is a scheme of its own.
/// </summary>
public sealed class StudentLinkTests
{
    private const string ServerKey = "test-server-key-that-is-at-least-32-chars";
    private const string Site = "https://www.vivacollege.in/gradesphereapp";
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly Guid _civil = Guid.NewGuid();
    private readonly SecretProtector _protector = new(ServerKey);
    private readonly StudentAccessSecrets _secrets = new(ServerKey);
    private readonly FakeSender _sender = new();
    private readonly Dictionary<string, Guid> _students = new();

    private sealed class FakeSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public bool Fail { get; set; }

        public Task SendAsync(EmailAccountSettings account, EmailMessage message, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("SMTP down");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private ApplicationDbContext Context(Guid? college)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_db).Options;
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.CollegeId).Returns(college);
        user.SetupGet(u => u.UserId).Returns(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        return new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, user.Object);
    }

    public StudentLinkTests()
    {
        using var context = Context(_college);
        context.Database.EnsureCreated(); // task types are seeded
        context.Colleges.Add(new College { CollegeId = _college, Name = "VIVA", CollegeCode = "VIVA", CollegeCenter = "Virar", ContactEmail = "a@b.in", ContactPhone = "1" });
        context.CourseMasters.Add(new CourseMaster { CourseId = _civil, Name = "Civil", CourseCode = "CIV", CollegeId = _college });
        Student(context, "2600001", "Asha", "asha@viva.in");
        Student(context, "2600002", "Ravi", "ravi@gmail.com", EmailVerification.Flagged);
        Student(context, "2600003", "Mina", null);
        Student(context, "2600004", "Gita", "gita@viva.in", active: false);
        Student(context, "2600005", "Neha", "neha@viva.in");
        context.EmailAccounts.Add(new EmailAccount
        {
            Id = Guid.NewGuid(), CollegeId = null, Host = "smtp-relay.gmail.com", Username = "gs@viva.in",
            PasswordProtected = _protector.Protect("app-password"), FromAddress = "noreply@viva.in",
        });
        context.CollegeSettings.Add(new CollegeSettings { Id = Guid.NewGuid(), CollegeId = _college, ExamOfficeEmail = "exam@viva.in" });
        context.SaveChanges();
    }

    private void Student(ApplicationDbContext context, string id, string first, string? email, string? status = null, bool active = true)
    {
        var s = new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = id, FirstName = first, LastName = "Patil", Email = email, EmailStatus = status, IsActive = active, CollegeId = _college, PhotoUrl = id == "2600001" ? "x.jpg" : null };
        _students[id] = s.StdMstId;
        context.StudentMasters.Add(s);
        context.StudentEligibilities.Add(new StudentEligibility { Id = Guid.NewGuid(), StdMstId = s.StdMstId, StudentId = id, CourseId = _civil, AYID = _ay, SemesterId = "Sem-1", Pattern = "NEP", CollegeId = _college });
    }

    private TaskLinkService Staff(ApplicationDbContext context) =>
        new(context, Outbox(context), new EmailAccountResolver(context, _protector));

    private EmailOutbox Outbox(ApplicationDbContext context) =>
        new(context, new EmailAccountResolver(context, _protector), _sender, NullLogger<EmailOutbox>.Instance);

    private StudentSignInService Students(ApplicationDbContext context) => new(context, _secrets, Outbox(context));

    private async Task RunQueueAsync()
    {
        using var context = Context(null);
        var renderers = new IEmailBodyRenderer[] { new TaskLinkEmailRenderer(context, _secrets), new OtpEmailRenderer(context, _secrets) };
        await new EmailQueueProcessor(context, new EmailAccountResolver(context, _protector), _sender, renderers, NullLogger<EmailQueueProcessor>.Instance)
            .ProcessAsync(100);
    }

    private SendLinksRequest Request(params string[] editable) => new() { AYID = _ay, CourseId = _civil, SemesterId = "Sem-1", Days = 14, EditableFields = editable.ToList() };

    /// <summary>Sends the class its links and the queue writes them; answers the token emailed to <paramref name="studentId"/>.</summary>
    private async Task<string> SendAndGetTokenAsync(string studentId = "2600001", params string[] editable)
    {
        using (var staff = Context(_college))
            await Staff(staff).SendAsync(Request(editable), Site);
        await RunQueueAsync();
        return TokenIn(_sender.Sent.Last(m => m.To == Email(studentId)));
    }

    private static string Email(string studentId) => studentId == "2600001" ? "asha@viva.in" : "neha@viva.in";

    private static string TokenIn(EmailMessage message) => Regex.Match(message.Text, @"/s/([A-Za-z0-9_-]{43})").Groups[1].Value;

    private static string CodeIn(EmailMessage message) => Regex.Match(message.Subject, @"^(\d{6}) ").Groups[1].Value;

    private async Task<StudentSignedIn> SignInAsync(string token)
    {
        using (var context = Context(null))
            await Students(context).SendCodeAsync(token);
        var code = CodeIn(_sender.Sent.Last());
        using var verify = Context(null);
        return await Students(verify).VerifyAsync(token, code);
    }

    private async Task<AuthenticateResult> AuthenticateAsync(string? session)
    {
        using var context = Context(null);
        var options = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Setup(o => o.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());
        var handler = new StudentSessionHandler(options.Object, NullLoggerFactory.Instance, UrlEncoder.Default, context, _secrets);
        var http = new DefaultHttpContext();
        if (session != null) http.Request.Headers[StudentAccessAuth.Header] = session;
        await handler.InitializeAsync(new AuthenticationScheme(StudentAccessAuth.Scheme, null, typeof(StudentSessionHandler)), http);
        return await handler.AuthenticateAsync();
    }

    // ------------------------------------------------------------------ sending

    [Fact]
    public async Task Sending_queues_a_link_for_each_student_with_a_usable_email_and_skips_the_rest()
    {
        using var context = Context(_college);
        var result = await Staff(context).SendAsync(Request(StudentFields.Photo, "unknown", StudentFields.Signature), Site);

        Assert.Equal((2, 1, 1, 1, 0), (result.Queued, result.NoEmail, result.Flagged, result.Inactive, result.Replaced));
        var batch = await context.TaskBatches.SingleAsync();
        Assert.Equal("photo,signature", batch.EditableFields);
        Assert.Equal(Site, batch.LinkBaseUrl);
        Assert.Equal("Verify student details — Civil, semester Sem-1", batch.Title);
        Assert.Equal(DateTime.Today.AddDays(14), batch.Deadline!.Value.ToLocalTime().Date);
        var links = await context.TaskLinks.ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.All(links, l => Assert.Null(l.TokenHash)); // made when the email is written
        Assert.Equal(2, await context.EmailQueue.CountAsync(m => m.Kind == EmailKinds.TaskLink && m.Priority == EmailPriorities.Low));
        Assert.Contains(await context.AuditLogs.Select(a => a.Event).ToListAsync(), e => e == "TaskLinksSent");
    }

    [Fact]
    public async Task Links_cannot_be_sent_without_an_email_account()
    {
        using var context = Context(_college);
        context.EmailAccounts.RemoveRange(context.EmailAccounts);
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Staff(context).SendAsync(Request(), Site));
        Assert.Contains("Email is not set up", ex.Message);
        Assert.Empty(context.TaskBatches);
    }

    [Fact]
    public async Task The_email_carries_a_link_to_the_student_site_and_only_its_hash_is_stored()
    {
        var token = await SendAndGetTokenAsync("2600001", StudentFields.Photo, StudentFields.Signature);

        var mail = _sender.Sent.Single(m => m.To == "asha@viva.in");
        Assert.Contains($"{Site}/s/{token}", mail.Html);
        Assert.Equal("VIVA: Update your photo and signature", mail.Subject);
        Assert.Contains("exam@viva.in", mail.Text);
        using var context = Context(null);
        var link = await context.TaskLinks.IgnoreQueryFilters().SingleAsync(l => l.StdMstId == _students["2600001"]);
        Assert.Equal(_secrets.Hash(token), link.TokenHash);
        Assert.NotNull(link.SentAt);
    }

    [Fact]
    public async Task A_new_batch_stops_the_students_earlier_link_and_its_session()
    {
        var first = await SendAndGetTokenAsync();
        var session = await SignInAsync(first);

        using (var staff = Context(_college))
            Assert.Equal(2, (await Staff(staff).SendAsync(Request(), Site)).Replaced);

        using var context = Context(null);
        Assert.Equal(LinkOpenStates.Closed, (await Students(context).OpenAsync(first)).State);
        Assert.False((await AuthenticateAsync(session.SessionToken)).Succeeded);
    }

    // ------------------------------------------------------------------ the student's side

    [Fact]
    public async Task Opening_a_link_shows_the_first_name_and_a_masked_email_and_an_unknown_one_is_invalid()
    {
        var token = await SendAndGetTokenAsync();
        using var context = Context(null);
        var info = await Students(context).OpenAsync(token);

        Assert.Equal((LinkOpenStates.Ready, "Asha", "as•••@viva.in", "VIVA"), (info.State, info.FirstName, info.MaskedEmail, info.College));
        Assert.Equal(LinkOpenStates.Invalid, (await Students(context).OpenAsync("x" + token[1..])).State);
        Assert.Equal(LinkOpenStates.Invalid, (await Students(context).OpenAsync(null)).State);
    }

    [Fact]
    public async Task The_right_code_signs_the_student_in_for_that_one_task()
    {
        var token = await SendAndGetTokenAsync();
        var signedIn = await SignInAsync(token);

        var result = await AuthenticateAsync(signedIn.SessionToken);
        Assert.True(result.Succeeded);
        var user = result.Principal!;
        Assert.Equal(_college.ToString(), user.FindFirstValue("CollegeId"));
        Assert.Equal(_students["2600001"].ToString(), user.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(user.IsInRole(StudentAccessAuth.RoleName));
        Assert.Equal(TaskTypes.StudentMaster, user.FindFirstValue(StudentAccessAuth.TaskClaim));

        using var context = Context(null);
        var link = await context.TaskLinks.IgnoreQueryFilters().SingleAsync(l => l.StdMstId == _students["2600001"]);
        Assert.NotNull(link.OpenedAt);
        Assert.Contains(await context.AuditLogs.IgnoreQueryFilters().Select(a => a.Event).ToListAsync(), e => e == "StudentSignedIn");
        var stored = await context.StudentSessions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(_secrets.Hash(signedIn.SessionToken), stored.SessionHash);
        Assert.True(stored.ExpiresAt <= DateTime.UtcNow + StudentAccessAuth.Idle);
    }

    [Fact]
    public async Task Wrong_codes_count_down_and_a_used_code_does_not_work_twice()
    {
        var token = await SendAndGetTokenAsync();
        using (var context = Context(null))
            await Students(context).SendCodeAsync(token);
        var code = CodeIn(_sender.Sent.Last());
        var wrong = code == "000000" ? "111111" : "000000";

        using (var context = Context(null))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Students(context).VerifyAsync(token, wrong));
            Assert.Equal("That code isn't right. 4 tries left.", ex.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Students(context).VerifyAsync(token, "12"));
        }
        using (var context = Context(null))
            await Students(context).VerifyAsync(token, code);
        using (var context = Context(null))
            Assert.Equal("Ask for a code first.", (await Assert.ThrowsAsync<InvalidOperationException>(() => Students(context).VerifyAsync(token, code))).Message);
    }

    [Fact]
    public async Task Five_wrong_tries_end_a_code()
    {
        var token = await SendAndGetTokenAsync();
        using (var context = Context(null))
            await Students(context).SendCodeAsync(token);
        var code = CodeIn(_sender.Sent.Last());
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < StudentSignInService.MaxTries; i++)
        {
            using var context = Context(null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Students(context).VerifyAsync(token, wrong));
        }
        using var last = Context(null);
        Assert.Equal("Too many wrong tries. Ask for a new code.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => Students(last).VerifyAsync(token, code))).Message);
    }

    [Fact]
    public async Task Codes_are_a_minute_apart_and_three_an_hour()
    {
        var token = await SendAndGetTokenAsync();
        using (var context = Context(null))
            await Students(context).SendCodeAsync(token);
        using (var context = Context(null))
            Assert.Contains("a moment ago", (await Assert.ThrowsAsync<InvalidOperationException>(() => Students(context).SendCodeAsync(token))).Message);

        using (var context = Context(null))
        {
            foreach (var o in context.OtpChallenges.IgnoreQueryFilters()) o.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
            context.OtpChallenges.Add(new OtpChallenge { Id = Guid.NewGuid(), CollegeId = _college, TaskLinkId = context.OtpChallenges.IgnoreQueryFilters().First().TaskLinkId, CreatedAt = DateTime.UtcNow.AddMinutes(-3), ExpiresAt = DateTime.UtcNow });
            context.OtpChallenges.Add(new OtpChallenge { Id = Guid.NewGuid(), CollegeId = _college, TaskLinkId = context.OtpChallenges.IgnoreQueryFilters().First().TaskLinkId, CreatedAt = DateTime.UtcNow.AddMinutes(-4), ExpiresAt = DateTime.UtcNow });
            await context.SaveChangesAsync();
        }
        using (var context = Context(null))
            Assert.Contains("3 codes in the last hour", (await Assert.ThrowsAsync<InvalidOperationException>(() => Students(context).SendCodeAsync(token))).Message);
    }

    [Fact]
    public async Task When_email_is_down_the_code_is_queued_and_written_fresh_when_it_goes()
    {
        var token = await SendAndGetTokenAsync();
        _sender.Fail = true;
        using (var context = Context(null))
            Assert.False((await Students(context).SendCodeAsync(token)).SentNow);
        _sender.Fail = false;
        using (var context = Context(null))
        {
            foreach (var m in context.EmailQueue.IgnoreQueryFilters().Where(m => m.Kind == EmailKinds.Otp)) m.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }
        await RunQueueAsync();

        var code = CodeIn(_sender.Sent.Last());
        using var verify = Context(null);
        Assert.False(string.IsNullOrEmpty((await Students(verify).VerifyAsync(token, code)).SessionToken));
    }

    [Fact]
    public async Task The_student_home_shows_their_details_and_what_they_may_change()
    {
        var token = await SendAndGetTokenAsync("2600001", StudentFields.Phone, StudentFields.Photo);
        var signedIn = await SignInAsync(token);
        var principal = (await AuthenticateAsync(signedIn.SessionToken)).Principal!;

        using var context = Context(_college); // the session's college, as the tenant filter sees it
        var home = await Students(context).HomeAsync(principal);
        Assert.Equal(("2600001", "Asha", true, false), (home.Student.StudentId, home.Student.FirstName, home.Student.HasPhoto, home.Student.HasSignature));
        Assert.Equal(new[] { StudentFields.Phone, StudentFields.Photo }, home.EditableFields);
        Assert.Equal("exam@viva.in", home.HelpEmail);

        await Students(context).SignOutAsync(principal);
        Assert.False((await AuthenticateAsync(signedIn.SessionToken)).Succeeded);
    }

    [Fact]
    public async Task No_header_is_no_result_and_a_made_up_session_fails()
    {
        Assert.True((await AuthenticateAsync(null)).None);
        Assert.False((await AuthenticateAsync(_secrets.NewToken())).Succeeded);
    }

    // ------------------------------------------------------------------ staff controls

    [Fact]
    public async Task Stopping_a_link_closes_it_and_ends_the_session_and_resending_makes_a_new_one()
    {
        var token = await SendAndGetTokenAsync();
        var session = await SignInAsync(token);
        Guid linkId;
        using (var context = Context(_college))
        {
            linkId = (await context.TaskLinks.SingleAsync(l => l.StdMstId == _students["2600001"])).Id;
            Assert.Equal(LinkStates.Stopped, (await Staff(context).StopLinkAsync(linkId)).State);
        }
        Assert.False((await AuthenticateAsync(session.SessionToken)).Succeeded);
        using (var context = Context(null))
            Assert.Equal(LinkOpenStates.Closed, (await Students(context).OpenAsync(token)).State);

        using (var context = Context(_college))
        {
            var fresh = await Staff(context).ResendAsync(linkId);
            Assert.NotEqual(linkId, fresh.Id);
            Assert.Equal(LinkStates.Queued, fresh.State);
        }
        await RunQueueAsync();
        var newToken = TokenIn(_sender.Sent.Last(m => m.To == "asha@viva.in"));
        using (var context = Context(null))
            Assert.Equal(LinkOpenStates.Ready, (await Students(context).OpenAsync(newToken)).State);
    }

    [Fact]
    public async Task Moving_the_deadline_reopens_closed_links_and_the_batch_shows_who_did_what()
    {
        var token = await SendAndGetTokenAsync();
        await SignInAsync(token);
        Guid batchId;
        using (var context = Context(_college))
        {
            batchId = (await context.TaskBatches.SingleAsync()).Id;
            foreach (var l in context.TaskLinks) l.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }
        using (var context = Context(null))
            Assert.Equal(LinkOpenStates.Closed, (await Students(context).OpenAsync(token)).State);

        using (var context = Context(_college))
        {
            var detail = await Staff(context).SetDeadlineAsync(batchId, DateTime.Today.AddDays(3));
            Assert.Equal(DateTime.Today.AddDays(3), detail.Deadline!.Value.ToLocalTime().Date);
            Assert.Equal((2, 1, 1), (detail.Total, detail.Opened, detail.NotOpened));
            Assert.Equal(new[] { LinkStates.Opened, LinkStates.Sent }, detail.Links.Select(l => l.State));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Staff(context).SetDeadlineAsync(batchId, DateTime.Today.AddDays(-1)));
        }
        using (var context = Context(null))
            Assert.Equal(LinkOpenStates.Ready, (await Students(context).OpenAsync(token)).State);
    }

    [Fact]
    public async Task Reminders_go_only_to_students_who_have_not_opened_their_link_and_stop_all_stops_the_rest()
    {
        var token = await SendAndGetTokenAsync();
        await SignInAsync(token);
        using var context = Context(_college);
        var batchId = (await context.TaskBatches.SingleAsync()).Id;

        Assert.Equal(1, await Staff(context).RemindAsync(batchId));
        Assert.Equal(0, await Staff(context).RemindAsync(batchId)); // already waiting in the queue
        Assert.Equal(2, await Staff(context).StopBatchAsync(batchId));
        Assert.All(await context.TaskLinks.ToListAsync(), l => Assert.Equal(TaskLinkStatuses.Revoked, l.Status));
        Assert.Empty(await context.EmailQueue.Where(m => m.Kind == EmailKinds.TaskLink && m.Status == EmailStatuses.Pending).ToListAsync());
        var list = await Staff(context).ListBatchesAsync(new LinkBatchFilter { AYID = _ay });
        Assert.Equal((2, false), (Assert.Single(list).Stopped, list[0].IsOpen));
    }

    // ------------------------------------------------------------------ the site address

    [Fact]
    public void Links_only_point_at_a_site_the_api_trusts()
    {
        var site = new StudentSiteUrl(null, new[] { "https://www.vivacollege.in" }, development: false);
        Assert.Equal(Site, site.Resolve(Site + "/"));
        Assert.Null(site.Resolve("https://evil.example/gradesphereapp"));
        Assert.Null(site.Resolve("http://www.vivacollege.in/gradesphereapp"));
        Assert.Null(site.Resolve(Site + "?x=1"));
        Assert.Null(site.Resolve(null));
        Assert.Null(site.Resolve("http://localhost:5173/ExamSoftware"));

        var dev = new StudentSiteUrl(null, Array.Empty<string>(), development: true);
        Assert.Equal("http://localhost:5173/ExamSoftware", dev.Resolve("http://localhost:5173/ExamSoftware/"));

        var configured = new StudentSiteUrl("https://vivacollege.in/ExamSoftware/", Array.Empty<string>(), development: false);
        Assert.Equal("https://vivacollege.in/ExamSoftware", configured.Resolve("https://evil.example"));
    }
}
