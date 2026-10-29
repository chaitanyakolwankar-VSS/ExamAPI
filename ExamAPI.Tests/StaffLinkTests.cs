using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using ExamAPI.Services.Staff;
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
/// Staff links: the exam office emails each person one link for a duty chart's supervision letters or an exam's examiner and
/// moderator letters, the queued email gets its token
/// when it is written, the person opens the link, gets a 6-digit code and signs in to a session of their own, checks
/// their details and downloads their own letters. Limits on codes and tries, and nobody sees anyone else's letters.
/// </summary>
public sealed class StaffLinkTests
{
    private const string ServerKey = "test-server-key-that-is-at-least-32-chars";
    private const string Site = "https://www.vivacollege.in/gradesphereapp";
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly Guid _computer = Guid.NewGuid();
    private readonly Guid _it = Guid.NewGuid();
    private readonly Guid _exam = Guid.NewGuid();       // the canonical copy (Computer)
    private readonly Guid _itExam = Guid.NewGuid();     // the IT branch's copy of the same exam
    private readonly Guid _oldExam = Guid.NewGuid();
    private readonly Guid _otherExam = Guid.NewGuid();
    private readonly Guid _chart = Guid.NewGuid();         // the December supervision chart (the newest)
    private readonly Guid _oldChart = Guid.NewGuid();
    private readonly Guid _otherChart = Guid.NewGuid();
    private readonly Guid _subject = Guid.NewGuid();
    private readonly Guid _asha = Guid.NewGuid();
    private readonly Guid _ravi = Guid.NewGuid();
    private readonly Guid _left = Guid.NewGuid();
    private readonly Guid _external = Guid.NewGuid();
    private readonly Guid _stranger = Guid.NewGuid();
    private readonly Guid _dean = Guid.NewGuid();
    private readonly Guid _physics = Guid.NewGuid();
    private readonly Guid _ashaSupervision = Guid.NewGuid();
    private readonly Guid _ashaExaminer = Guid.NewGuid();
    private readonly Guid _ashaOld = Guid.NewGuid();
    private readonly Guid _raviModerator = Guid.NewGuid();
    private readonly Guid _raviSupervision = Guid.NewGuid();
    private readonly Guid _strangerLetter = Guid.NewGuid();
    private readonly SecretProtector _protector = new(ServerKey);
    private readonly StudentAccessSecrets _secrets = new(ServerKey);
    private readonly FakeSender _sender = new();
    private readonly Mock<IStaffLetterPdfService> _pdf = new();

    private sealed class FakeSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();

        public Task SendAsync(EmailAccountSettings account, EmailMessage message, CancellationToken ct = default)
        {
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

    private static string Date(int daysFromToday) => DateTime.Today.AddDays(daysFromToday).ToString("dd-MM-yyyy");

    public StaffLinkTests()
    {
        _pdf.Setup(p => p.OneAsync(It.IsAny<Guid>())).ReturnsAsync((new byte[] { 0x25, 0x50, 0x44, 0x46 }, "letter.pdf"));

        using var context = Context(_college);
        context.Database.EnsureCreated();
        context.Colleges.Add(new College { CollegeId = _college, Name = "VIVA Institute of Technology", CollegeCode = "VIT", CollegeCenter = "Virar", ContactEmail = "a@b.in", ContactPhone = "1" });
        context.Colleges.Add(new College { CollegeId = _otherCollege, Name = "Other College", CollegeCode = "OTH", CollegeCenter = "Pune", ContactEmail = "o@o.in", ContactPhone = "2" });
        context.CourseMasters.Add(new CourseMaster { CourseId = _computer, Name = "Computer", CourseCode = "CMP", CollegeId = _college });
        context.CourseMasters.Add(new CourseMaster { CourseId = _it, Name = "Information Technology", CourseCode = "IT", CollegeId = _college });
        // Created one after the other (the save stamps the time): the old chart and exam, then December (Computer is the canonical copy), then its IT copy and the December chart.
        context.DutyCharts.Add(new DutyChart { Id = _oldChart, CollegeId = _college, AYID = _ay, Name = "April 2026 Supervision", FromDate = DateTime.Today.AddDays(-200), ToDate = DateTime.Today.AddDays(-190) });
        context.Exams.Add(new ExamMaster { ExamId = _oldExam, Name = "April 2026", ExamType = "Regular", AcademicYearAYID = _ay, CourseId = _computer, IsActive = true, CollegeId = _college });
        context.SaveChanges();
        Thread.Sleep(20);
        context.Exams.Add(new ExamMaster { ExamId = _exam, Name = "December 2026", ExamType = "Regular", AcademicYearAYID = _ay, CourseId = _computer, IsActive = true, CollegeId = _college });
        context.SaveChanges();
        Thread.Sleep(20);
        context.Exams.Add(new ExamMaster { ExamId = _itExam, Name = "December 2026", ExamType = "Regular", AcademicYearAYID = _ay, CourseId = _it, IsActive = true, CollegeId = _college });
        context.Exams.Add(new ExamMaster { ExamId = _otherExam, Name = "December 2026", ExamType = "Regular", AcademicYearAYID = _ay, IsActive = true, CollegeId = _otherCollege });
        context.SaveChanges();
        Thread.Sleep(20);
        context.DutyCharts.Add(new DutyChart { Id = _chart, CollegeId = _college, AYID = _ay, Name = "December 2026 Supervision", FromDate = DateTime.Today.AddDays(10), ToDate = DateTime.Today.AddDays(20) });
        context.DutyCharts.Add(new DutyChart { Id = _otherChart, CollegeId = _otherCollege, AYID = _ay, Name = "December 2026 Supervision", FromDate = DateTime.Today, ToDate = DateTime.Today.AddDays(5) });
        context.SubjectMasters.Add(new SubjectMaster { SubjectId = _subject, SubjectCode = "CSC601", Name = "System Programming", SemId = "Sem-6", Pattern = "NEP", CourseId = _computer, CollegeId = _college });
        context.TimeTables.Add(new TimeTableMaster { Id = Guid.NewGuid(), ExamId = _exam, CourseId = _computer, SubjectId = _subject, Date = Date(10), Time = "10:00 AM - 01:00 PM", CollegeId = _college });
        context.TimeTables.Add(new TimeTableMaster { Id = Guid.NewGuid(), ExamId = _itExam, CourseId = _it, Date = Date(20), Time = "10:00 AM - 01:00 PM", CollegeId = _college });

        context.StaffOptions.Add(new StaffOption { Id = _dean, CollegeId = _college, Kind = StaffOptionKinds.Designation, Name = "Dean" });
        context.StaffOptions.Add(new StaffOption { Id = _physics, CollegeId = _college, Kind = StaffOptionKinds.Department, Name = "Physics" });
        Person(context, _asha, "E101", "Prof.", "Asha", "R.", "Patil", "asha@viva.in");
        Person(context, _ravi, "E102", "Dr.", "Ravi", null, "Kumar", "ravi@viva.in");
        Person(context, _left, "E103", "Mr.", "Left", null, "Person", "left@viva.in", active: false);
        Person(context, _external, null, "Dr.", "Sunil", null, "Rane", "sunil@other.in", external: true);
        Person(context, _stranger, "X1", "Mr.", "Stranger", null, "Other", "stranger@other.in", college: _otherCollege);

        // Supervision letters belong to a duty chart, examiner and moderator letters to an exam.
        context.StaffLetters.Add(Letter(_ashaSupervision, _asha, StaffLetterTypes.Supervision, 1, "Prof. Asha R. Patil", chart: _chart, role: SupervisionRoles.Junior, duties: 2, examTitle: "December 2026 Supervision"));
        context.StaffLetters.Add(Letter(_ashaExaminer, _asha, StaffLetterTypes.Examiner, 1, "Prof. Asha R. Patil", exam: _exam, subject: _subject));
        context.StaffLetters.Add(Letter(_ashaOld, _asha, StaffLetterTypes.Supervision, 2, "Prof. Asha R. Patil", chart: _oldChart, role: SupervisionRoles.Senior, duties: 1, examTitle: "April 2026 Supervision"));
        context.StaffLetters.Add(Letter(_raviModerator, _ravi, StaffLetterTypes.Moderator, 2, "Dr. Ravi Kumar", exam: _itExam, subject: _subject));
        context.StaffLetters.Add(Letter(_raviSupervision, _ravi, StaffLetterTypes.Supervision, 4, "Dr. Ravi Kumar", chart: _chart, role: SupervisionRoles.NonTeaching, duties: 1, examTitle: "December 2026 Supervision"));
        context.StaffLetters.Add(Letter(Guid.NewGuid(), _left, StaffLetterTypes.Supervision, 3, "Mr. Left Person", chart: _chart, role: SupervisionRoles.Junior, duties: 1, examTitle: "December 2026 Supervision"));
        context.StaffLetters.Add(Letter(Guid.NewGuid(), _left, StaffLetterTypes.Examiner, 3, "Mr. Left Person", exam: _exam, subject: _subject));
        context.StaffLetters.Add(Letter(Guid.NewGuid(), _external, StaffLetterTypes.Examiner, 2, "Dr. Sunil Rane", exam: _exam, subject: _subject));
        context.StaffLetters.Add(Letter(_strangerLetter, _stranger, StaffLetterTypes.Supervision, 1, "Mr. Stranger Other", chart: _otherChart, role: SupervisionRoles.Junior, duties: 1, college: _otherCollege, examTitle: "December 2026 Supervision"));

        context.EmailAccounts.Add(new EmailAccount
        {
            Id = Guid.NewGuid(), CollegeId = null, Host = "smtp-relay.gmail.com", Username = "gs@viva.in",
            PasswordProtected = _protector.Protect("app-password"), FromAddress = "noreply@viva.in",
        });
        context.CollegeSettings.Add(new CollegeSettings { Id = Guid.NewGuid(), CollegeId = _college, ExamOfficeEmail = "exam@viva.in" });
        context.SaveChanges();
    }

    private void Person(ApplicationDbContext context, Guid id, string? code, string title, string first, string? middle, string last, string email,
        bool active = true, bool external = false, Guid? college = null) =>
        context.StaffMasters.Add(new StaffMaster
        {
            StaffId = id, CollegeId = college ?? _college, EmployeeCode = code, Title = title, FirstName = first, MiddleName = middle, LastName = last,
            Email = email, IsActive = active, IsExternal = external, ExternalCollege = external ? "Mumbai College" : null, ExternalCity = external ? "Mumbai" : null,
        });

    private StaffLetter Letter(Guid id, Guid staff, string type, int serial, string name, Guid? exam = null, Guid? chart = null, string? role = null, int duties = 0,
        Guid? subject = null, string examTitle = "December 2026 Regular", Guid? college = null)
    {
        var letter = new StaffLetter
        {
            Id = id, CollegeId = college ?? _college, StaffId = staff, LetterType = type, ExamId = exam, DutyChartId = chart, AYID = _ay, SubjectId = subject, SerialNo = serial,
            LetterNo = $"VIT/EXAM/2026-27/{StaffLetterTypes.Code(type)}-{serial:D3}", LetterDate = new DateTime(2026, 10, 20), Role = role,
            StaffName = name, ExamTitle = examTitle,
        };
        for (var i = 0; i < duties; i++)
            letter.Duties.Add(new StaffLetterDuty { Id = Guid.NewGuid(), CollegeId = college ?? _college, LetterId = id, Date = Date(10 + i), Time = "10:00 AM - 01:00 PM", SortOrder = i });
        return letter;
    }

    private EmailOutbox Outbox(ApplicationDbContext context) =>
        new(context, new EmailAccountResolver(context, _protector), _sender, NullLogger<EmailOutbox>.Instance);

    private StaffLinkService Office(ApplicationDbContext context) =>
        new(context, Outbox(context), new EmailAccountResolver(context, _protector));

    private StaffSignInService SignIn(ApplicationDbContext context) => new(context, _secrets, Outbox(context));

    private StaffPortalService Portal(ApplicationDbContext context) => new(context, _pdf.Object);

    private async Task RunQueueAsync()
    {
        using var context = Context(null);
        var renderers = new IEmailBodyRenderer[]
        {
            new TaskLinkEmailRenderer(context, _secrets), new OtpEmailRenderer(context, _secrets),
            new StaffLinkEmailRenderer(context, _secrets), new StaffOtpEmailRenderer(context, _secrets),
        };
        await new EmailQueueProcessor(context, new EmailAccountResolver(context, _protector), _sender, renderers, NullLogger<EmailQueueProcessor>.Instance)
            .ProcessAsync(100);
    }

    /// <summary>The exam's examiner and moderator letters (asked through the IT copy of the exam).</summary>
    private StaffEmailRequest Request(string kind = "all", bool onlyNotSent = false, params Guid[] staff) =>
        new() { ExamId = _itExam, Kind = kind, OnlyNotSent = onlyNotSent, StaffIds = staff.Length == 0 ? null : staff.ToList() };

    /// <summary>The December duty chart's supervision letters.</summary>
    private StaffEmailRequest ChartRequest(string kind = "supervision", bool onlyNotSent = false, params Guid[] staff) =>
        new() { DutyChartId = _chart, Kind = kind, OnlyNotSent = onlyNotSent, StaffIds = staff.Length == 0 ? null : staff.ToList() };

    private async Task<StaffEmailResult> SendAsync(StaffEmailRequest request)
    {
        using var context = Context(_college);
        return await Office(context).SendAsync(request, Site);
    }

    private static string TokenIn(EmailMessage message) => Regex.Match(message.Text, @"/t#([A-Za-z0-9_-]{43})").Groups[1].Value;

    private static string CodeIn(EmailMessage message) => Regex.Match(message.Subject, @"^(\d{6}) ").Groups[1].Value;

    /// <summary>Sends the person their link and the queue writes it; answers the token emailed to them.</summary>
    private async Task<string> SendAndGetTokenAsync(string email = "asha@viva.in", Guid? staff = null, bool chart = false)
    {
        await SendAsync(chart ? ChartRequest(staff: staff ?? _asha) : Request(staff: staff ?? _asha));
        await RunQueueAsync();
        return TokenIn(_sender.Sent.Last(m => m.To == email));
    }

    private async Task<StaffSignedIn> SignInAsync(string token)
    {
        using (var context = Context(null))
            await SignIn(context).SendCodeAsync(token);
        var code = CodeIn(_sender.Sent.Last());
        using var verify = Context(null);
        return await SignIn(verify).VerifyAsync(token, code);
    }

    private async Task<AuthenticateResult> AuthenticateAsync(string? session)
    {
        using var context = Context(null);
        var options = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Setup(o => o.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());
        var handler = new StaffSessionHandler(options.Object, NullLoggerFactory.Instance, UrlEncoder.Default, context, _secrets);
        var http = new DefaultHttpContext();
        if (session != null) http.Request.Headers[StaffAccessAuth.Header] = session;
        await handler.InitializeAsync(new AuthenticationScheme(StaffAccessAuth.Scheme, null, typeof(StaffSessionHandler)), http);
        return await handler.AuthenticateAsync();
    }

    /// <summary>The claims of a session as the handler would give them (college, person, link, session).</summary>
    private ClaimsPrincipal Session(Guid link, Guid staff, Guid? college = null) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, staff.ToString()),
        new Claim(ClaimTypes.Role, StaffAccessAuth.RoleName),
        new Claim("CollegeId", (college ?? _college).ToString()),
        new Claim(StaffAccessAuth.LinkClaim, link.ToString()),
        new Claim(StaffAccessAuth.SessionClaim, Guid.NewGuid().ToString()),
    }, StaffAccessAuth.Scheme));

    private async Task<Guid> LinkIdAsync(Guid staff)
    {
        using var context = Context(null);
        return (await context.StaffLinks.IgnoreQueryFilters().Where(l => l.StaffId == staff && l.Status == StaffLinkStatuses.Active).SingleAsync()).Id;
    }

    // ------------------------------------------------------------------ sending

    [Fact]
    public async Task Sending_makes_one_link_and_one_email_for_each_person_however_many_letters_they_have()
    {
        var result = await SendAsync(Request());

        // Asha (3 letters, 2 for this exam), Ravi (his copy is the IT branch's), the external examiner; the one who left is skipped.
        Assert.Equal((3, 1), (result.Queued, result.Skipped));
        using var context = Context(null);
        var links = await context.StaffLinks.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(3, links.Count);
        Assert.All(links, l => Assert.Equal((_exam, StaffLinkStatuses.Active, (Guid?)_college), (l.ExamId, l.Status, l.CollegeId)));   // the canonical exam, whichever copy was asked
        Assert.All(links, l => Assert.Null(l.DutyChartId));
        Assert.All(links, l => Assert.Null(l.TokenHash));                                                                           // the token is made when the email is written
        Assert.Equal(new[] { "asha@viva.in", "ravi@viva.in", "sunil@other.in" }, (await context.EmailQueue.IgnoreQueryFilters().Where(m => m.Kind == StaffEmailKinds.Link).Select(m => m.ToAddress).ToListAsync()).OrderBy(x => x));
        var audit = await context.AuditLogs.IgnoreQueryFilters().SingleAsync(a => a.Event == "StaffLettersEmailed");
        Assert.Contains("3 links", audit.Details);
    }

    [Fact]
    public async Task The_link_works_for_thirty_days_after_the_last_timetable_date_and_at_least_a_week()
    {
        await SendAsync(Request(staff: _asha));
        using (var context = Context(null))
        {
            var link = await context.StaffLinks.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(DateTime.Today.AddDays(20 + 30), link.ExpiresAt.ToLocalTime().Date);   // the IT copy's paper is the last one
        }

        using (var context = Context(_college))
        {
            foreach (var t in await context.TimeTables.ToListAsync()) t.Date = Date(-100);
            await context.SaveChangesAsync();
        }
        await SendAsync(Request(staff: _asha));
        using (var context = Context(null))
        {
            var link = await context.StaffLinks.IgnoreQueryFilters().SingleAsync(l => l.Status == StaffLinkStatuses.Active);
            Assert.Equal(DateTime.Today.AddDays(StaffLinkService.MinDays), link.ExpiresAt.ToLocalTime().Date);

            context.TimeTables.RemoveRange(await context.TimeTables.IgnoreQueryFilters().ToListAsync());
            await context.SaveChangesAsync();
        }
        await SendAsync(Request(staff: _asha));
        using (var context = Context(null))
        {
            var link = await context.StaffLinks.IgnoreQueryFilters().SingleAsync(l => l.Status == StaffLinkStatuses.Active);
            Assert.Equal(DateTime.Today.AddDays(StaffLinkService.AfterExamDays), link.ExpiresAt.ToLocalTime().Date);   // no dates at all
        }
    }

    [Fact]
    public async Task The_email_names_the_letters_and_holds_the_token_only_in_what_was_sent()
    {
        var token = await SendAndGetTokenAsync();

        var mail = _sender.Sent.Last(m => m.To == "asha@viva.in");
        Assert.Equal("VIVA Institute of Technology: your letters for December 2026 Regular", mail.Subject);
        Assert.Contains("Dear Asha,", mail.Text);
        Assert.Contains("Examiner — CSC601 System Programming", mail.Text);
        Assert.DoesNotContain("Supervision duty", mail.Text);                // supervision letters go with a duty chart's link
        Assert.DoesNotContain("April 2026", mail.Text);                      // only this exam's letters are listed
        Assert.Contains("Open my letters", mail.Html);
        Assert.Contains($"{Site}/t#{token}", mail.Text);

        using var context = Context(null);
        var link = await context.StaffLinks.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(_secrets.Hash(token), link.TokenHash);                  // only the hash is stored
        Assert.NotNull(link.SentAt);
        var queued = await context.EmailQueue.IgnoreQueryFilters().SingleAsync(m => m.Kind == StaffEmailKinds.Link);
        Assert.Equal(EmailStatuses.Sent, queued.Status);
        Assert.Null(queued.HtmlBody);
        Assert.Null(queued.TextBody);
    }

    [Fact]
    public async Task Sending_again_stops_the_older_link_and_its_unsent_email()
    {
        var first = await SendAndGetTokenAsync();
        await SendAsync(Request(staff: _asha));
        using (var context = Context(null))
        {
            var states = await context.StaffLinks.IgnoreQueryFilters().OrderBy(l => l.CreatedAt).Select(l => l.Status).ToListAsync();
            Assert.Equal(new[] { StaffLinkStatuses.Revoked, StaffLinkStatuses.Active }, states);
        }
        await RunQueueAsync();
        var second = TokenIn(_sender.Sent.Last(m => m.To == "asha@viva.in"));
        Assert.NotEqual(first, second);

        using var open = Context(null);
        Assert.Equal(LinkOpenStates.Closed, (await SignIn(open).OpenAsync(first)).State);
        Assert.Equal(LinkOpenStates.Ready, (await SignIn(open).OpenAsync(second)).State);

        // A third send before the queue ran: the second email is cancelled, never sent.
        var before = _sender.Sent.Count;
        await SendAsync(Request(staff: _asha));
        await SendAsync(Request(staff: _asha));
        await RunQueueAsync();
        Assert.Equal(before + 1, _sender.Sent.Count);
    }

    [Fact]
    public async Task Sending_again_ends_a_session_at_once()
    {
        var token = await SendAndGetTokenAsync();
        var signedIn = await SignInAsync(token);
        Assert.True((await AuthenticateAsync(signedIn.SessionToken)).Succeeded);

        await SendAsync(Request(staff: _asha));

        Assert.False((await AuthenticateAsync(signedIn.SessionToken)).Succeeded);
    }

    [Fact]
    public async Task Preview_counts_people_emails_and_who_was_emailed_already()
    {
        using (var context = Context(_college))
        {
            var preview = await Office(context).PreviewAsync(Request());
            Assert.Equal((4, 3, 0, 4), (preview.People, preview.WithEmail, preview.AlreadySent, preview.Letters));   // Asha, Ravi, the external examiner and the one who left: one letter each
            Assert.Equal(new[] { "Mr. Left Person" }, preview.NoEmail);
            Assert.True(preview.SenderReady);

            var appointment = await Office(context).PreviewAsync(Request("appointment"));
            Assert.Equal((4, 3, 1), (appointment.People, appointment.WithEmail, appointment.NoEmail.Count));
        }

        await SendAsync(Request(staff: _asha));
        using (var context = Context(_college))
        {
            var preview = await Office(context).PreviewAsync(Request());
            Assert.Equal((3, 1), (preview.WithEmail, preview.AlreadySent));
            var onlyAsha = await Office(context).PreviewAsync(Request(staff: _asha));
            Assert.Equal((1, 1, 1, 1), (onlyAsha.People, onlyAsha.WithEmail, onlyAsha.AlreadySent, onlyAsha.Letters));
        }
    }

    [Fact]
    public async Task Only_not_sent_leaves_out_the_people_already_emailed()
    {
        await SendAsync(Request(staff: _asha));

        var result = await SendAsync(Request(onlyNotSent: true));

        Assert.Equal((2, 2), (result.Queued, result.Skipped));       // Ravi and the external examiner; Asha was emailed, the one who left has no usable address
        using var context = Context(null);
        Assert.Equal(1, await context.StaffLinks.IgnoreQueryFilters().CountAsync(l => l.StaffId == _asha));
        var again = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(Request(onlyNotSent: true)));
        Assert.Equal("Everyone has been emailed already.", again.Message);
    }

    [Fact]
    public async Task An_empty_list_of_people_is_nobody_not_everybody()
    {
        using var context = Context(_college);
        var request = Request();
        request.StaffIds = new List<Guid>();

        Assert.Equal(0, (await Office(context).PreviewAsync(request)).People);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Office(context).SendAsync(request, Site));
        Assert.Equal("There are no letters to email for this selection.", ex.Message);
        Assert.Empty(await context.StaffLinks.ToListAsync());
    }

    [Fact]
    public async Task Without_an_email_account_nothing_is_sent_and_the_preview_says_so()
    {
        using (var context = Context(null))
        {
            context.EmailAccounts.RemoveRange(await context.EmailAccounts.ToListAsync());
            await context.SaveChangesAsync();
        }

        using var staff = Context(_college);
        Assert.False((await Office(staff).PreviewAsync(Request())).SenderReady);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Office(staff).SendAsync(Request(), Site));
        Assert.Contains("Email is not set up yet", ex.Message);
        Assert.Empty(await staff.StaffLinks.ToListAsync());
    }

    [Fact]
    public async Task Unknown_kinds_and_other_colleges_exams_are_refused()
    {
        using var context = Context(_college);
        var kind = await Assert.ThrowsAsync<InvalidOperationException>(() => Office(context).PreviewAsync(Request("everything")));
        Assert.Contains("supervision letters", kind.Message);

        var other = Request();
        other.ExamId = _otherExam;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Office(context).PreviewAsync(other));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Office(context).StatusAsync(_otherExam, null));

        var otherChart = ChartRequest();
        otherChart.DutyChartId = _otherChart;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Office(context).PreviewAsync(otherChart));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Office(context).SendAsync(otherChart, Site));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Office(context).StatusAsync(null, _otherChart));
        var unknownChart = ChartRequest();
        unknownChart.DutyChartId = Guid.NewGuid();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Office(context).PreviewAsync(unknownChart));
    }

    [Fact]
    public async Task Another_colleges_staff_are_never_in_the_preview()
    {
        using var context = Context(_college);
        var request = Request(staff: _stranger);
        request.ExamId = _exam;
        Assert.Equal(0, (await Office(context).PreviewAsync(request)).People);

        Assert.Equal(0, (await Office(context).PreviewAsync(ChartRequest(staff: _stranger))).People);
    }

    [Fact]
    public async Task A_request_is_for_exactly_one_of_an_exam_and_a_duty_chart_and_the_kind_must_fit()
    {
        using var context = Context(_college);
        var both = Request();
        both.DutyChartId = _chart;
        var neither = Request();
        neither.ExamId = null;

        var tooMany = await Assert.ThrowsAsync<InvalidOperationException>(() => Office(context).PreviewAsync(both));
        Assert.Equal("Choose a duty chart (for supervision letters) or an exam (for examiner letters).", tooMany.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Office(context).PreviewAsync(neither));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Office(context).SendAsync(both, Site));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Office(context).StatusAsync(null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Office(context).StatusAsync(_exam, _chart));

        var supervisionOfAnExam = await Assert.ThrowsAsync<InvalidOperationException>(() => Office(context).PreviewAsync(Request("supervision")));
        Assert.Equal("Supervision letters belong to a duty chart, not an exam.", supervisionOfAnExam.Message);
        var examinerOfAChart = await Assert.ThrowsAsync<InvalidOperationException>(() => Office(context).PreviewAsync(ChartRequest("appointment")));
        Assert.Equal("Examiner and moderator letters belong to an exam, not a duty chart.", examinerOfAChart.Message);
        Assert.Empty(await context.StaffLinks.ToListAsync());
    }

    // ------------------------------------------------------------------ a duty chart's supervision letters

    [Fact]
    public async Task A_chart_preview_counts_the_people_with_supervision_letters_in_it()
    {
        using var context = Context(_college);

        var preview = await Office(context).PreviewAsync(ChartRequest());
        Assert.Equal((3, 2, 0, 3), (preview.People, preview.WithEmail, preview.AlreadySent, preview.Letters));   // Asha, Ravi and the one who left: one letter each (the old chart's letter is not counted)
        Assert.Equal(new[] { "Mr. Left Person" }, preview.NoEmail);
        Assert.Equal(3, (await Office(context).PreviewAsync(ChartRequest("all"))).People);                       // "all" of a chart is its supervision letters
        Assert.Equal(1, (await Office(context).PreviewAsync(ChartRequest(staff: _ravi))).People);
    }

    [Fact]
    public async Task Sending_for_a_chart_makes_a_link_for_the_chart_which_ends_a_month_after_its_last_day()
    {
        var result = await SendAsync(ChartRequest());

        Assert.Equal((2, 1), (result.Queued, result.Skipped));       // Asha and Ravi; the one who left has no usable address
        using var context = Context(null);
        var links = await context.StaffLinks.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(new[] { _asha, _ravi }.OrderBy(x => x), links.Select(l => l.StaffId).OrderBy(x => x));
        Assert.All(links, l => Assert.Equal(((Guid?)null, (Guid?)_chart, StaffLinkStatuses.Active), (l.ExamId, l.DutyChartId, l.Status)));
        Assert.All(links, l => Assert.Equal(DateTime.Today.AddDays(20 + StaffLinkService.AfterExamDays), l.ExpiresAt.ToLocalTime().Date));
        var audit = await context.AuditLogs.IgnoreQueryFilters().SingleAsync(a => a.Event == "StaffLettersEmailed");
        Assert.Equal(_chart.ToString(), audit.RecordId);
        Assert.Contains("2 links", audit.Details);
    }

    [Fact]
    public async Task A_chart_link_lasts_at_least_a_week_even_when_the_chart_is_over()
    {
        using (var context = Context(_college))
        {
            (await context.DutyCharts.SingleAsync(c => c.Id == _chart)).ToDate = DateTime.Today.AddDays(-100);
            await context.SaveChangesAsync();
        }

        await SendAsync(ChartRequest(staff: _asha));

        using var check = Context(null);
        var link = await check.StaffLinks.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(DateTime.Today.AddDays(StaffLinkService.MinDays), link.ExpiresAt.ToLocalTime().Date);
    }

    [Fact]
    public async Task The_chart_email_names_the_chart_and_lists_only_its_supervision_letters()
    {
        var token = await SendAndGetTokenAsync(chart: true);

        var mail = _sender.Sent.Last(m => m.To == "asha@viva.in");
        Assert.Equal("VIVA Institute of Technology: your letters for December 2026 Supervision", mail.Subject);
        Assert.Contains("Supervision duty (Junior Supervisor, 2 dates)", mail.Text);
        Assert.DoesNotContain("Examiner", mail.Text);                        // her examiner letter goes with the exam's link
        Assert.DoesNotContain("April 2026", mail.Text);                      // and the earlier chart's letter with its own
        Assert.Contains($"{Site}/t#{token}", mail.Text);
    }

    [Fact]
    public async Task A_person_has_one_link_for_an_exam_and_one_for_a_chart_and_sending_again_replaces_only_its_own()
    {
        await SendAsync(Request(staff: _asha));
        await SendAsync(ChartRequest(staff: _asha));
        using (var context = Context(null))
        {
            var links = await context.StaffLinks.IgnoreQueryFilters().Where(l => l.Status == StaffLinkStatuses.Active).ToListAsync();
            Assert.Equal(2, links.Count);                                    // neither stopped the other
            Assert.Equal(new[] { false, true }, links.Select(l => l.DutyChartId != null).OrderBy(x => x));
        }

        await SendAsync(ChartRequest(staff: _asha));
        using (var context = Context(null))
        {
            var all = await context.StaffLinks.IgnoreQueryFilters().OrderBy(l => l.CreatedAt).ToListAsync();
            Assert.Equal(3, all.Count);
            Assert.Equal(new[] { StaffLinkStatuses.Active, StaffLinkStatuses.Revoked, StaffLinkStatuses.Active }, all.Select(l => l.Status));
            Assert.Equal(2, all.Count(l => l.Status == StaffLinkStatuses.Active));
        }
    }

    [Fact]
    public async Task Only_not_sent_works_per_chart()
    {
        await SendAsync(ChartRequest(staff: _asha));

        var result = await SendAsync(ChartRequest(onlyNotSent: true));

        Assert.Equal((1, 2), (result.Queued, result.Skipped));              // Ravi; Asha was emailed, the one who left has no usable address
        using var context = Context(_college);
        Assert.Equal(1, (await Office(context).PreviewAsync(ChartRequest(staff: _asha))).AlreadySent);
    }

    [Fact]
    public async Task Opening_a_chart_link_names_the_chart()
    {
        var token = await SendAndGetTokenAsync(chart: true);

        using var context = Context(null);
        var info = await SignIn(context).OpenAsync(token);

        Assert.Equal((LinkOpenStates.Ready, "Your letters for December 2026 Supervision"), (info.State, info.TaskName));
    }

    [Fact]
    public async Task A_chart_link_marks_the_charts_letters_as_current_in_the_portal()
    {
        await SendAndGetTokenAsync(chart: true);
        var link = await LinkIdAsync(_asha);

        using var context = Context(_college);
        var home = await Portal(context).HomeAsync(Session(link, _asha));

        Assert.Equal("December 2026 Supervision", home.ExamTitle);
        Assert.Equal(new[] { _ashaSupervision, _ashaExaminer, _ashaOld }, home.Letters.Select(l => l.LetterId));   // the same letters, whichever link
        Assert.Equal(new[] { true, false, false }, home.Letters.Select(l => l.Current));
    }

    [Fact]
    public async Task The_queue_does_not_write_an_email_for_a_chart_link_whose_letters_were_cancelled()
    {
        await SendAsync(ChartRequest(staff: _asha));
        using (var context = Context(_college))
        {
            foreach (var l in await context.StaffLetters.Where(l => l.StaffId == _asha && l.DutyChartId == _chart).ToListAsync()) l.IsDeleted = true;
            await context.SaveChangesAsync();
        }
        var before = _sender.Sent.Count;

        await RunQueueAsync();

        Assert.Equal(before, _sender.Sent.Count);
        using var check = Context(null);
        Assert.Equal(EmailStatuses.Cancelled, (await check.EmailQueue.IgnoreQueryFilters().SingleAsync(m => m.Kind == StaffEmailKinds.Link)).Status);
    }

    [Fact]
    public async Task Chart_status_follows_the_latest_chart_link()
    {
        using (var context = Context(_college))
            Assert.Empty(await Office(context).StatusAsync(null, _chart));

        await SendAsync(ChartRequest(staff: _asha));
        await RunQueueAsync();
        using (var context = Context(_college))
        {
            var sent = Assert.Single(await Office(context).StatusAsync(null, _chart));
            Assert.Equal(_asha, sent.StaffId);
            Assert.NotNull(sent.SentAt);
            Assert.Null(sent.OpenedAt);
            Assert.Empty(await Office(context).StatusAsync(_exam, null));    // an exam link of hers would be separate
        }
    }

    [Fact]
    public async Task The_signed_in_college_is_needed_to_send()
    {
        using var context = Context(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Office(context).SendAsync(Request(), Site));
    }

    // ------------------------------------------------------------------ the link page

    [Fact]
    public async Task Opening_the_link_shows_a_first_name_and_a_masked_email_only()
    {
        var token = await SendAndGetTokenAsync();

        using var context = Context(null);
        var info = await SignIn(context).OpenAsync(token);

        Assert.Equal(LinkOpenStates.Ready, info.State);
        Assert.Equal(("Asha", "as•••@viva.in", "VIVA Institute of Technology", "STAFF_LETTERS", "Your letters for December 2026 Regular", "exam@viva.in"),
            (info.FirstName, info.MaskedEmail, info.College, info.TaskType, info.TaskName, info.HelpEmail));
        Assert.NotNull(info.Deadline);
        Assert.Null(info.UpdatedAt);
        Assert.Equal(LinkOpenStates.Invalid, (await SignIn(context).OpenAsync("not-a-token")).State);
        Assert.Equal(LinkOpenStates.Invalid, (await SignIn(context).OpenAsync(null)).State);
    }

    [Fact]
    public async Task The_right_code_signs_the_person_in_for_that_one_link()
    {
        var token = await SendAndGetTokenAsync();
        var signedIn = await SignInAsync(token);

        var result = await AuthenticateAsync(signedIn.SessionToken);
        Assert.True(result.Succeeded);
        var user = result.Principal!;
        Assert.Equal(_college.ToString(), user.FindFirstValue("CollegeId"));
        Assert.Equal(_asha.ToString(), user.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(user.IsInRole(StaffAccessAuth.RoleName));
        Assert.Equal((await LinkIdAsync(_asha)).ToString(), user.FindFirstValue(StaffAccessAuth.LinkClaim));

        using var context = Context(null);
        var link = await context.StaffLinks.IgnoreQueryFilters().SingleAsync();
        Assert.NotNull(link.OpenedAt);
        Assert.Contains(await context.AuditLogs.IgnoreQueryFilters().Select(a => a.Event).ToListAsync(), e => e == "StaffSignedIn");
        var stored = await context.StaffSessions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(_secrets.Hash(signedIn.SessionToken), stored.SessionHash);
        Assert.True(stored.ExpiresAt <= DateTime.UtcNow + StaffAccessAuth.Idle);
        Assert.False((await AuthenticateAsync("some-other-session")).Succeeded);
        Assert.Equal(AuthenticateResult.NoResult().None, (await AuthenticateAsync(null)).None);
    }

    [Fact]
    public async Task A_code_works_once_and_a_wrong_code_counts_a_try()
    {
        var token = await SendAndGetTokenAsync();
        using (var context = Context(null))
            await SignIn(context).SendCodeAsync(token);
        var code = CodeIn(_sender.Sent.Last());
        var wrong = code == "000000" ? "111111" : "000000";

        using (var context = Context(null))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SignIn(context).VerifyAsync(token, wrong));
            Assert.Equal("That code isn't right. 4 tries left.", ex.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => SignIn(context).VerifyAsync(token, "12"));
            await SignIn(context).VerifyAsync(token, code);
            var again = await Assert.ThrowsAsync<InvalidOperationException>(() => SignIn(context).VerifyAsync(token, code));
            Assert.Equal("Ask for a code first.", again.Message);
        }
    }

    [Fact]
    public async Task Five_wrong_tries_use_the_code_up()
    {
        var token = await SendAndGetTokenAsync();
        using (var context = Context(null))
            await SignIn(context).SendCodeAsync(token);
        var code = CodeIn(_sender.Sent.Last());
        var wrong = code == "000000" ? "111111" : "000000";

        using var attempts = Context(null);
        for (var i = 0; i < StaffSignInService.MaxTries; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => SignIn(attempts).VerifyAsync(token, wrong));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SignIn(attempts).VerifyAsync(token, code));   // even the right one
        Assert.Equal("Too many wrong tries. Ask for a new code.", ex.Message);
    }

    [Fact]
    public async Task Codes_are_limited_to_one_a_minute_and_three_an_hour()
    {
        var token = await SendAndGetTokenAsync();
        using (var context = Context(null))
        {
            await SignIn(context).SendCodeAsync(token);
            var soon = await Assert.ThrowsAsync<InvalidOperationException>(() => SignIn(context).SendCodeAsync(token));
            Assert.Contains("a moment ago", soon.Message);

            // Pretend the earlier codes were asked 5 minutes apart.
            var challenges = await context.StaffOtps.IgnoreQueryFilters().OrderBy(o => o.CreatedAt).ToListAsync();
            challenges[0].CreatedAt = DateTime.UtcNow.AddMinutes(-10);
            await context.SaveChangesAsync();
            await SignIn(context).SendCodeAsync(token);
            foreach (var (c, i) in (await context.StaffOtps.IgnoreQueryFilters().OrderBy(o => o.CreatedAt).ToListAsync()).Select((c, i) => (c, i)))
                c.CreatedAt = DateTime.UtcNow.AddMinutes(-20 + i * 5);
            await context.SaveChangesAsync();
            await SignIn(context).SendCodeAsync(token);   // the third

            foreach (var c in await context.StaffOtps.IgnoreQueryFilters().ToListAsync()) c.CreatedAt = c.CreatedAt.AddMinutes(-5);
            await context.SaveChangesAsync();
            var many = await Assert.ThrowsAsync<InvalidOperationException>(() => SignIn(context).SendCodeAsync(token));
            Assert.Contains("3 codes in the last hour", many.Message);
        }
    }

    [Fact]
    public async Task The_code_is_stored_only_as_a_keyed_hash_and_never_in_the_queue()
    {
        var token = await SendAndGetTokenAsync();
        using (var context = Context(null))
            await SignIn(context).SendCodeAsync(token);
        var code = CodeIn(_sender.Sent.Last());

        using var check = Context(null);
        var challenge = await check.StaffOtps.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(_secrets.CodeHash(challenge.Id, code), challenge.CodeHash);
        var row = await check.EmailQueue.IgnoreQueryFilters().SingleAsync(m => m.Kind == StaffEmailKinds.Otp);
        Assert.Equal(EmailStatuses.Sent, row.Status);
        Assert.Null(row.HtmlBody);
        Assert.Null(row.TextBody);
        Assert.Equal(_college, row.CollegeId);
    }

    [Fact]
    public async Task A_slow_code_email_is_queued_and_written_with_a_fresh_code_when_it_goes()
    {
        var token = await SendAndGetTokenAsync();
        var failing = new Mock<IEmailSender>();
        failing.Setup(s => s.SendAsync(It.IsAny<EmailAccountSettings>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("SMTP down"));
        CodeSent sent;
        using (var context = Context(null))
        {
            var outbox = new EmailOutbox(context, new EmailAccountResolver(context, _protector), failing.Object, NullLogger<EmailOutbox>.Instance);
            sent = await new StaffSignInService(context, _secrets, outbox).SendCodeAsync(token);
        }
        Assert.False(sent.SentNow);
        Assert.Contains("a minute or two", sent.Message);

        var before = _sender.Sent.Count;
        await RunQueueAsync();
        var code = CodeIn(_sender.Sent.Last());
        Assert.Equal(before + 1, _sender.Sent.Count);
        using var verify = Context(null);
        Assert.False(string.IsNullOrEmpty((await SignIn(verify).VerifyAsync(token, code)).SessionToken));
    }

    [Fact]
    public async Task An_expired_or_stopped_link_does_not_open_send_codes_or_keep_a_session()
    {
        var token = await SendAndGetTokenAsync();
        var signedIn = await SignInAsync(token);

        using (var context = Context(null))
        {
            var link = await context.StaffLinks.IgnoreQueryFilters().SingleAsync();
            link.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();

            var info = await SignIn(context).OpenAsync(token);
            Assert.Equal(LinkOpenStates.Closed, info.State);
            Assert.Contains("closed", info.Message);
            Assert.Null(info.FirstName);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SignIn(context).SendCodeAsync(token));
            Assert.Contains("closed", ex.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => SignIn(context).VerifyAsync(token, "123456"));
        }
        Assert.False((await AuthenticateAsync(signedIn.SessionToken)).Succeeded);   // checked on every request
    }

    [Fact]
    public async Task A_revoked_link_and_a_person_who_left_are_refused()
    {
        var token = await SendAndGetTokenAsync();
        var signedIn = await SignInAsync(token);
        using (var context = Context(null))
        {
            var staff = await context.StaffMasters.IgnoreQueryFilters().SingleAsync(s => s.StaffId == _asha);
            staff.IsActive = false;
            await context.SaveChangesAsync();

            Assert.Equal(LinkOpenStates.Closed, (await SignIn(context).OpenAsync(token)).State);
            await Assert.ThrowsAsync<InvalidOperationException>(() => SignIn(context).SendCodeAsync(token));
        }
        Assert.False((await AuthenticateAsync(signedIn.SessionToken)).Succeeded);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var token = await SendAndGetTokenAsync();
        var signedIn = await SignInAsync(token);
        var principal = (await AuthenticateAsync(signedIn.SessionToken)).Principal!;

        using (var context = Context(_college))
            await SignIn(context).SignOutAsync(principal);

        Assert.False((await AuthenticateAsync(signedIn.SessionToken)).Succeeded);
    }

    // ------------------------------------------------------------------ after sign-in

    [Fact]
    public async Task Me_shows_only_the_persons_own_letters_and_marks_the_ones_of_the_links_exam()
    {
        await SendAndGetTokenAsync();
        var link = await LinkIdAsync(_asha);

        using var context = Context(_college);
        var home = await Portal(context).HomeAsync(Session(link, _asha));

        Assert.Equal(("Asha", "as•••@viva.in", "December 2026 Regular", "VIVA Institute of Technology", true),
            (home.FirstName, home.MaskedEmail, home.ExamTitle, home.CollegeName, home.NeedsDetails));
        Assert.Equal(3, home.Letters.Count);
        Assert.DoesNotContain(home.Letters, l => l.LetterId == _raviModerator || l.LetterId == _strangerLetter);
        Assert.Equal(new[] { _ashaSupervision, _ashaExaminer, _ashaOld }, home.Letters.Select(l => l.LetterId));   // newest exam first, supervision before examiner
        Assert.Equal(new[] { false, true, false }, home.Letters.Select(l => l.Current));   // this link is for the exam: only her examiner letter is current
        Assert.Equal(new[] { "Supervision", "Examiner", "Supervision" }, home.Letters.Select(l => l.Kind));
        Assert.Equal("Supervision duty — Junior Supervisor, 2 dates", home.Letters[0].Title);
        Assert.Equal("Examiner — CSC601 System Programming", home.Letters[1].Title);
        Assert.Equal("Supervision duty — Senior Supervisor, 1 date", home.Letters[2].Title);
        Assert.Equal("April 2026 Supervision", home.Letters[2].ExamTitle);
        Assert.Equal(("VIT/EXAM/2026-27/DUTY-001", "2026-10-20"), (home.Letters[0].LetterNo, home.Letters[0].LetterDate));

        Assert.Equal(("Prof.", "Asha", "R.", "Patil", false), (home.Details.Title, home.Details.FirstName, home.Details.MiddleName, home.Details.LastName, home.Details.IsExternal));
        Assert.Equal(StaffTitles.All.ToList(), home.Options.Titles);
        Assert.Equal(new[] { "Physics" }, home.Options.Departments.Select(d => d.Name));
        Assert.Equal(new[] { "Dean" }, home.Options.Designations.Select(d => d.Name));
    }

    [Fact]
    public async Task A_cancelled_letter_is_not_shown()
    {
        await SendAndGetTokenAsync();
        var link = await LinkIdAsync(_asha);
        using (var context = Context(_college))
        {
            var letter = await context.StaffLetters.SingleAsync(l => l.Id == _ashaExaminer);
            letter.IsDeleted = true;
            await context.SaveChangesAsync();
        }

        using var read = Context(_college);
        var home = await Portal(read).HomeAsync(Session(link, _asha));
        Assert.DoesNotContain(home.Letters, l => l.LetterId == _ashaExaminer);
    }

    [Fact]
    public async Task A_session_of_another_college_sees_nothing()
    {
        await SendAndGetTokenAsync();
        var link = await LinkIdAsync(_asha);

        using var context = Context(_otherCollege);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Portal(context).HomeAsync(Session(link, _asha, _otherCollege)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Portal(context).LetterPdfAsync(Session(link, _asha, _otherCollege), _ashaSupervision));
    }

    [Fact]
    public async Task Saving_the_details_updates_staff_master_the_letters_and_the_link()
    {
        await SendAndGetTokenAsync();
        var link = await LinkIdAsync(_asha);

        StaffHome home;
        using (var context = Context(_college))
        {
            home = await Portal(context).SaveDetailsAsync(Session(link, _asha), new StaffDetailsView
            {
                Title = "dr", FirstName = "  Asha ", MiddleName = "", LastName = "Patil-Rao", DepartmentId = _physics, DesignationId = _dean,
                IsExternal = true, ExternalCollege = "Should be ignored", ExternalCity = "Nowhere",
            });
        }

        Assert.False(home.NeedsDetails);
        Assert.Equal(("Dr.", "Asha", null, "Patil-Rao", _physics, _dean), (home.Details.Title, home.Details.FirstName, home.Details.MiddleName, home.Details.LastName, home.Details.DepartmentId, home.Details.DesignationId));
        Assert.False(home.Details.IsExternal);                         // not changeable here
        Assert.Null(home.Details.ExternalCollege);                     // our own staff have no college of their own to give

        using var check = Context(null);
        var staff = await check.StaffMasters.IgnoreQueryFilters().SingleAsync(s => s.StaffId == _asha);
        Assert.Equal(("E101", "asha@viva.in", false), (staff.EmployeeCode, staff.Email, staff.IsExternal));    // never changed here
        var letters = await check.StaffLetters.IgnoreQueryFilters().Where(l => l.StaffId == _asha).ToListAsync();
        Assert.All(letters, l => Assert.Equal("Dr. Asha Patil-Rao", l.StaffName));
        Assert.All(await check.StaffLetters.IgnoreQueryFilters().Where(l => l.StaffId == _ravi).ToListAsync(), l => Assert.Equal("Dr. Ravi Kumar", l.StaffName));
        Assert.NotNull((await check.StaffLinks.IgnoreQueryFilters().SingleAsync(l => l.StaffId == _asha)).DetailsConfirmedAt);
        var audit = await check.AuditLogs.IgnoreQueryFilters().SingleAsync(a => a.Event == "StaffDetailsConfirmed");
        Assert.Equal((_college, _asha, StaffAccessAuth.RoleName), (audit.CollegeId, audit.PerformedBy, audit.UserType));
        Assert.Contains("title", audit.Details);
        Assert.Contains("last name", audit.Details);
    }

    [Fact]
    public async Task Confirming_without_changes_still_marks_the_details_checked()
    {
        await SendAndGetTokenAsync();
        var link = await LinkIdAsync(_asha);

        using var context = Context(_college);
        var home = await Portal(context).HomeAsync(Session(link, _asha));
        var again = await Portal(context).SaveDetailsAsync(Session(link, _asha), home.Details);

        Assert.False(again.NeedsDetails);
        Assert.Contains("nothing changed", (await context.AuditLogs.SingleAsync(a => a.Event == "StaffDetailsConfirmed")).Details);
        Assert.Equal("Prof. Asha R. Patil", (await context.StaffLetters.SingleAsync(l => l.Id == _ashaSupervision)).StaffName);
    }

    [Fact]
    public async Task An_external_person_may_change_their_college_and_city()
    {
        await SendAndGetTokenAsync("sunil@other.in", _external);
        var link = await LinkIdAsync(_external);

        using var context = Context(_college);
        var home = await Portal(context).SaveDetailsAsync(Session(link, _external), new StaffDetailsView
        {
            Title = "Dr.", FirstName = "Sunil", LastName = "Rane", IsExternal = false, ExternalCollege = " Pune  College ", ExternalCity = "Pune",
        });

        Assert.Equal((true, "Pune College", "Pune"), (home.Details.IsExternal, home.Details.ExternalCollege, home.Details.ExternalCity));
        var staff = await context.StaffMasters.SingleAsync(s => s.StaffId == _external);
        Assert.Equal((true, "Pune College", "Pune"), (staff.IsExternal, staff.ExternalCollege, staff.ExternalCity));
    }

    [Fact]
    public async Task Bad_details_are_refused_in_plain_words_and_nothing_is_saved()
    {
        await SendAndGetTokenAsync();
        var link = await LinkIdAsync(_asha);

        using var context = Context(_college);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Portal(context).SaveDetailsAsync(Session(link, _asha), new StaffDetailsView
        {
            Title = "Lord", FirstName = "", LastName = new string('x', 60), DepartmentId = Guid.NewGuid(), DesignationId = Guid.NewGuid(),
        }));
        Assert.Contains("Choose your title from the list", ex.Message);
        Assert.Contains("Enter your first name.", ex.Message);
        Assert.Contains("longer than 50 characters", ex.Message);
        Assert.Contains("Choose your department from the list.", ex.Message);
        Assert.Contains("Choose your designation from the list.", ex.Message);

        var missingTitle = await Assert.ThrowsAsync<InvalidOperationException>(() => Portal(context).SaveDetailsAsync(Session(link, _asha), new StaffDetailsView { FirstName = "Asha" }));
        Assert.Contains("Choose your title.", missingTitle.Message);

        var staff = await context.StaffMasters.SingleAsync(s => s.StaffId == _asha);
        Assert.Equal(("Prof.", "Asha", "Patil"), (staff.Title, staff.FirstName, staff.LastName));
        Assert.Null((await context.StaffLinks.SingleAsync(l => l.Id == link)).DetailsConfirmedAt);
    }

    [Fact]
    public async Task An_external_person_must_give_their_college()
    {
        await SendAndGetTokenAsync("sunil@other.in", _external);
        var link = await LinkIdAsync(_external);

        using var context = Context(_college);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Portal(context).SaveDetailsAsync(Session(link, _external), new StaffDetailsView
        {
            Title = "Dr.", FirstName = "Sunil", LastName = "Rane", ExternalCollege = " ",
        }));
        Assert.Equal("Enter your college.", ex.Message);
    }

    [Fact]
    public async Task A_person_downloads_their_own_letters_and_the_first_download_is_recorded()
    {
        await SendAndGetTokenAsync();
        var link = await LinkIdAsync(_asha);

        using (var context = Context(_college))
        {
            var pdf = await Portal(context).LetterPdfAsync(Session(link, _asha), _ashaOld);   // an earlier exam's letter too
            Assert.Equal("letter.pdf", pdf.FileName);
            Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, pdf.Bytes);
        }
        DateTime? first;
        using (var context = Context(null))
        {
            first = (await context.StaffLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == link)).DownloadedAt;
            Assert.NotNull(first);
            Assert.Single(await context.AuditLogs.IgnoreQueryFilters().Where(a => a.Event == "StaffLetterDownloaded").ToListAsync());
        }

        using (var context = Context(_college))
            await Portal(context).LetterPdfAsync(Session(link, _asha), _ashaSupervision);
        using (var context = Context(null))
        {
            Assert.Equal(first, (await context.StaffLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == link)).DownloadedAt);   // only the first counts
            Assert.Single(await context.AuditLogs.IgnoreQueryFilters().Where(a => a.Event == "StaffLetterDownloaded").ToListAsync());
        }
        _pdf.Verify(p => p.OneAsync(It.IsAny<Guid>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Someone_elses_letter_and_a_cancelled_one_are_not_found_and_make_no_pdf()
    {
        await SendAndGetTokenAsync();
        var link = await LinkIdAsync(_asha);

        using (var context = Context(_college))
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => Portal(context).LetterPdfAsync(Session(link, _asha), _raviModerator));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => Portal(context).LetterPdfAsync(Session(link, _asha), _strangerLetter));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => Portal(context).LetterPdfAsync(Session(link, _asha), Guid.NewGuid()));
            (await context.StaffLetters.SingleAsync(l => l.Id == _ashaExaminer)).IsDeleted = true;
            await context.SaveChangesAsync();
            await Assert.ThrowsAsync<KeyNotFoundException>(() => Portal(context).LetterPdfAsync(Session(link, _asha), _ashaExaminer));
        }

        _pdf.Verify(p => p.OneAsync(It.IsAny<Guid>()), Times.Never);
        using var check = Context(null);
        Assert.Null((await check.StaffLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == link)).DownloadedAt);
    }

    [Fact]
    public async Task A_session_no_longer_open_gets_nothing_from_the_portal()
    {
        await SendAndGetTokenAsync();
        var link = await LinkIdAsync(_asha);
        await SendAsync(Request(staff: _asha));      // replaces the link

        using var context = Context(_college);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Portal(context).HomeAsync(Session(link, _asha)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Portal(context).LetterPdfAsync(Session(link, _asha), _ashaSupervision));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Portal(context).HomeAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    // ------------------------------------------------------------------ status

    [Fact]
    public async Task Status_follows_the_latest_link_from_emailed_to_opened_to_downloaded()
    {
        using (var context = Context(_college))
            Assert.Empty(await Office(context).StatusAsync(_itExam, null));

        await SendAsync(Request(staff: _asha));
        using (var context = Context(_college))
        {
            var queued = Assert.Single(await Office(context).StatusAsync(_itExam, null));
            Assert.Equal(_asha, queued.StaffId);
            Assert.Null(queued.SentAt);                                                                                       // waiting in the queue
            Assert.Null(queued.OpenedAt);
            Assert.Null(queued.DownloadedAt);
        }

        await RunQueueAsync();
        var token = TokenIn(_sender.Sent.Last(m => m.To == "asha@viva.in"));
        using (var context = Context(_college))
        {
            var sent = Assert.Single(await Office(context).StatusAsync(_itExam, null));
            Assert.NotNull(sent.SentAt);
            Assert.Null(sent.OpenedAt);
        }

        await SignInAsync(token);
        var link = await LinkIdAsync(_asha);
        using (var context = Context(_college))
            await Portal(context).LetterPdfAsync(Session(link, _asha), _ashaSupervision);
        using (var context = Context(_college))
        {
            var done = Assert.Single(await Office(context).StatusAsync(_exam, null));      // either copy of the exam will do
            Assert.NotNull(done.SentAt);
            Assert.NotNull(done.OpenedAt);
            Assert.NotNull(done.DownloadedAt);
        }

        await SendAsync(Request(staff: _asha));                                       // a new link starts again
        using (var context = Context(_college))
        {
            var again = Assert.Single(await Office(context).StatusAsync(_itExam, null));
            Assert.Null(again.SentAt);
            Assert.Null(again.OpenedAt);
            Assert.Null(again.DownloadedAt);
        }
    }

    [Fact]
    public async Task The_queue_does_not_write_an_email_for_a_link_that_was_replaced_or_whose_letters_were_cancelled()
    {
        await SendAsync(Request(staff: _asha));
        using (var context = Context(_college))
        {
            foreach (var l in await context.StaffLetters.Where(l => l.StaffId == _asha && l.ExamId == _exam).ToListAsync()) l.IsDeleted = true;
            await context.SaveChangesAsync();
        }
        var before = _sender.Sent.Count;

        await RunQueueAsync();

        Assert.Equal(before, _sender.Sent.Count);
        using var check = Context(null);
        Assert.Equal(EmailStatuses.Cancelled, (await check.EmailQueue.IgnoreQueryFilters().SingleAsync(m => m.Kind == StaffEmailKinds.Link)).Status);
        Assert.Null((await check.StaffLinks.IgnoreQueryFilters().SingleAsync()).TokenHash);
    }

    [Fact]
    public void The_staff_session_has_a_scheme_header_and_policy_of_its_own()
    {
        Assert.NotEqual(StudentAccessAuth.Scheme, StaffAccessAuth.Scheme);
        Assert.NotEqual(StudentAccessAuth.Header, StaffAccessAuth.Header);
        Assert.NotEqual(StudentAccessAuth.Policy, StaffAccessAuth.Policy);
        Assert.Equal("X-Staff-Session", StaffAccessAuth.Header);
    }
}
