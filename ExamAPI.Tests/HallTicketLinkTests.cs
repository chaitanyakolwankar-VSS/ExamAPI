using System.Security.Claims;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using ExamAPI.Services.Files;
using ExamAPI.Services.GenerateHallTicket;
using ExamAPI.Services.StudentAccess;
using ExamAPI.Services.StudentMasters;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>
/// Hall ticket links: staff email the students of a released exam their hall ticket link; the student sees the
/// hall ticket (with ERN and ABC ID) once their photo and signature are on record, may only add a missing one,
/// and the first download is recorded. Also the ABC ID field.
/// </summary>
public sealed class HallTicketLinkTests : IDisposable
{
    private const string ServerKey = "test-server-key-that-is-at-least-32-chars";
    private const string Site = "https://www.vivacollege.in/gradesphereapp";
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly string _base = Path.Combine(Path.GetTempPath(), "hall-ticket-links-" + Guid.NewGuid().ToString("N"));
    private readonly FileStorage _storage;
    private readonly SecretProtector _protector = new(ServerKey);
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _course = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly Guid _exam = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();
    private static readonly string PngDataUrl = "data:image/png;base64," + Convert.ToBase64String(UploadsTests.Png);

    private ApplicationDbContext Context(Guid? college)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_db).Options;
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.CollegeId).Returns(college);
        return new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, user.Object);
    }

    public HallTicketLinkTests()
    {
        Directory.CreateDirectory(_base);
        _storage = new FileStorage(_base, null, Path.Combine(_base, "store"));
        using var context = Context(_college);
        context.Database.EnsureCreated();
        context.Colleges.Add(new College { CollegeId = _college, Name = "VIVA", CollegeCode = "VIVA", CollegeCenter = "Virar", ContactEmail = "a@b.in", ContactPhone = "1" });
        context.CourseMasters.Add(new CourseMaster { CourseId = _course, Name = "Civil Engineering", CourseCode = "CIV", CollegeId = _college });
        context.Exams.Add(new ExamMaster { ExamId = _exam, Name = "DEC 2026", ExamType = "Regular", IsActive = true, CourseId = _course, CollegeId = _college });
        context.StudentMasters.Add(new StudentMaster { StdMstId = _student, StudentId = "2400001", FirstName = "Asha", LastName = "Patil", Email = "asha@viva.in", StudentPRN = "ERN-1", AbcId = "123456789012", CollegeId = _college });
        context.MarksMasters.Add(new MarksMaster { MarksId = Guid.NewGuid(), StudentID = "2400001", StdMstId = _student, ExamId = _exam, SemesterId = "Sem-1", Pattern = "NEP - R-24", AcademicYearAYID = _ay, SeatNo = "1001", CollegeId = _college });
        context.EmailAccounts.Add(new EmailAccount
        {
            Id = Guid.NewGuid(), CollegeId = null, Host = "smtp-relay.gmail.com", Username = "gs@viva.in",
            PasswordProtected = _protector.Protect("app-password"), FromAddress = "noreply@viva.in",
        });
        context.SaveChanges();
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    /// <summary>A written-exam (ESE) subject the student sits, with its timetable date and time (blank = not saved).</summary>
    private async Task<Guid> SubjectAsync(string code, string date, string time, Guid? student = null)
    {
        using var context = Context(_college);
        var subject = new SubjectMaster { SubjectId = Guid.NewGuid(), SubjectCode = code, Name = code, SemId = "Sem-1", Pattern = "NEP - R-24", CourseId = _course, CollegeId = _college };
        var creditsId = Guid.NewGuid();
        context.SubjectMasters.Add(subject);
        context.SubjectCreditMasters.Add(new SubjectCreditMaster
        {
            CreditsId = creditsId, SubjectId = subject.SubjectId, TotalCredits = "4", AYID = _ay.ToString(), CollegeId = _college,
            Credits = new List<SubjectCredits> { new() { Id = Guid.NewGuid(), Head = "H1", HeadType = "ESE", HeadOutOf = "80", HeadPass = "32", CreditsId = creditsId } },
        });
        if (date != "")
            context.TimeTables.Add(new TimeTableMaster { Id = Guid.NewGuid(), ExamId = _exam, CourseId = _course, SubjectId = subject.SubjectId, Date = date, Time = time, CollegeId = _college });
        var marks = await context.MarksMasters.SingleAsync(m => m.StdMstId == (student ?? _student));
        context.StudentMarks.Add(new StudentMarks { Id = Guid.NewGuid(), MarksId = marks.MarksId, SubjectId = subject.SubjectId, Head = "H1" });
        await context.SaveChangesAsync();
        return subject.SubjectId;
    }

    private HallTicketLinksRequest Request() => new() { Ayid = _ay, CourseId = _course, Semester = "Sem-1", Pattern = "NEP - R-24", ExamId = _exam, Days = 30 };

    private TaskLinkService Staff(ApplicationDbContext context)
    {
        var resolver = new EmailAccountResolver(context, _protector);
        return new TaskLinkService(context, new EmailOutbox(context, resolver, new Mock<IEmailSender>().Object, NullLogger<EmailOutbox>.Instance), resolver, new GenerateHallTicketService(context));
    }

    /// <summary>Sends the hall ticket links and answers the student's link id.</summary>
    private async Task<Guid> SendAsync()
    {
        using var context = Context(_college);
        await Staff(context).SendHallTicketsAsync(Request(), Site);
        return (await context.TaskLinks.SingleAsync(l => l.TaskTypeCode == TaskTypes.HallTicket && l.Status == TaskLinkStatuses.Active)).Id;
    }

    private ClaimsPrincipal Student(Guid link) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, _student.ToString()),
        new Claim(ClaimTypes.Role, StudentAccessAuth.RoleName),
        new Claim("CollegeId", _college.ToString()),
        new Claim(StudentAccessAuth.LinkClaim, link.ToString()),
        new Claim(StudentAccessAuth.SessionClaim, Guid.NewGuid().ToString()),
        new Claim(StudentAccessAuth.TaskClaim, TaskTypes.HallTicket),
    }, StudentAccessAuth.Scheme));

    private StudentHallTicketService HallTickets(ApplicationDbContext context) => new(context, new GenerateHallTicketService(context), _storage);

    private StudentDetailsService Details(ApplicationDbContext context)
    {
        var resolver = new EmailAccountResolver(context, _protector);
        var outbox = new EmailOutbox(context, resolver, new Mock<IEmailSender>().Object, NullLogger<EmailOutbox>.Instance);
        return new StudentDetailsService(context, new StudentSignInService(context, new StudentAccessSecrets(ServerKey), outbox), outbox, _storage);
    }

    [Fact]
    public async Task Links_go_without_a_release_step_and_name_the_exam_without_stopping_a_details_link()
    {
        using (var context = Context(_college))
        {
            var details = new TaskBatch { Id = Guid.NewGuid(), CollegeId = _college, TaskTypeCode = TaskTypes.StudentMaster, Title = "d", Deadline = DateTime.UtcNow.AddDays(3) };
            context.TaskBatches.Add(details);
            context.TaskLinks.Add(new TaskLink { Id = Guid.NewGuid(), CollegeId = _college, TaskTypeCode = TaskTypes.StudentMaster, BatchId = details.Id, StdMstId = _student, ExpiresAt = DateTime.UtcNow.AddDays(3) });
            await context.SaveChangesAsync();

            var result = await Staff(context).SendHallTicketsAsync(Request(), Site);
            Assert.Equal((1, 0), (result.Queued, result.Replaced));
            var batch = await context.TaskBatches.SingleAsync(b => b.Id == result.BatchId);
            Assert.Equal((TaskTypes.HallTicket, (Guid?)_exam), (batch.TaskTypeCode, batch.RefId));
            Assert.Contains("DEC 2026", batch.Title);
            Assert.Equal(2, await context.TaskLinks.CountAsync(l => l.Status == TaskLinkStatuses.Active));

            // A second send for the same exam replaces the first hall ticket link only.
            Assert.Equal(1, (await Staff(context).SendHallTicketsAsync(Request(), Site)).Replaced);
        }
    }

    [Fact]
    public async Task The_hall_ticket_shows_once_photo_and_signature_are_added_and_has_ern_and_abc_id()
    {
        var link = await SendAsync();
        using var context = Context(_college);

        var first = await HallTickets(context).GetAsync(Student(link));
        Assert.False(first.Ready);
        Assert.Equal(new[] { "photo", "signature" }, first.Missing);
        Assert.Equal(("DEC 2026", "Regular", "Civil Engineering", "NEP - R-24"), (first.ExamName, first.ExamType, first.Branch, first.Pattern));

        await Details(context).SaveImageAsync(Student(link), StudentImageKinds.Photo, PngDataUrl);
        await Details(context).SaveImageAsync(Student(link), StudentImageKinds.Signature, PngDataUrl);
        Assert.Contains("already on record", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Details(context).SaveImageAsync(Student(link), StudentImageKinds.Photo, PngDataUrl))).Message);
        Assert.Null((await context.TaskLinks.SingleAsync(l => l.Id == link)).UsedAt); // adding a picture isn't a download

        var ready = await HallTickets(context).GetAsync(Student(link));
        Assert.True(ready.Ready);
        Assert.Equal(("1001", "ERN-1", "123456789012"), (ready.Student!.seat, ready.Student.ern, ready.Student.abcId));

        await HallTickets(context).MarkDownloadedAsync(Student(link));
        var used = (await context.TaskLinks.SingleAsync(l => l.Id == link)).UsedAt;
        Assert.NotNull(used);
        await HallTickets(context).MarkDownloadedAsync(Student(link));
        Assert.Equal(used, (await context.TaskLinks.SingleAsync(l => l.Id == link)).UsedAt);
        Assert.Equal(1, await context.AuditLogs.CountAsync(a => a.Event == "HallTicketDownloaded"));
    }

    [Fact]
    public async Task Stopping_the_links_takes_the_hall_ticket_back()
    {
        var link = await SendAsync();
        using var context = Context(_college);
        var batch = (await context.TaskLinks.SingleAsync(l => l.Id == link)).BatchId!.Value;
        await Staff(context).StopBatchAsync(batch);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => HallTickets(context).GetAsync(Student(link)));
    }

    [Fact]
    public async Task A_subject_the_students_sit_without_a_date_blocks_sending_until_the_timetable_is_saved()
    {
        await SubjectAsync("CE301", "03-12-2026", "10 AM");
        var blank = await SubjectAsync("CE302", "", "");
        using var context = Context(_college);

        var preview = await Staff(context).PreviewHallTicketsAsync(Request());
        Assert.Equal(new[] { "CE302" }, preview.MissingTimetable);
        Assert.Equal((1, 1, 0), (preview.Students, preview.Sendable, preview.Emailed));
        Assert.Contains("CE302", (await Assert.ThrowsAsync<InvalidOperationException>(() => Staff(context).SendHallTicketsAsync(Request(), Site))).Message);

        context.TimeTables.Add(new TimeTableMaster { Id = Guid.NewGuid(), ExamId = _exam, CourseId = _course, SubjectId = blank, Date = "04-12-2026", Time = "10 AM", CollegeId = _college });
        await context.SaveChangesAsync();
        Assert.Equal(1, (await Staff(context).SendHallTicketsAsync(Request(), Site)).Queued);
        Assert.Equal(1, (await Staff(context).PreviewHallTicketsAsync(Request())).Emailed);
    }

    [Fact]
    public async Task One_student_or_only_those_who_have_not_downloaded_can_be_sent_again()
    {
        var other = Guid.NewGuid();
        using (var context = Context(_college))
        {
            context.StudentMasters.Add(new StudentMaster { StdMstId = other, StudentId = "2400002", FirstName = "Ravi", LastName = "More", Email = "ravi@viva.in", CollegeId = _college });
            context.MarksMasters.Add(new MarksMaster { MarksId = Guid.NewGuid(), StudentID = "2400002", StdMstId = other, ExamId = _exam, SemesterId = "Sem-1", Pattern = "NEP - R-24", AcademicYearAYID = _ay, SeatNo = "1002", CollegeId = _college });
            await context.SaveChangesAsync();

            var one = await Staff(context).SendHallTicketsAsync(new HallTicketLinksRequest { Ayid = _ay, CourseId = _course, Semester = "Sem-1", Pattern = "NEP - R-24", ExamId = _exam, Student = "2400002" }, Site);
            Assert.Equal(1, one.Queued);
            Assert.Contains("Ravi", (await context.TaskBatches.SingleAsync(b => b.Id == one.BatchId)).Title);
            Assert.Contains("no hall ticket", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Staff(context).SendHallTicketsAsync(new HallTicketLinksRequest { Ayid = _ay, CourseId = _course, Semester = "Sem-1", Pattern = "NEP - R-24", ExamId = _exam, Student = "9999999" }, Site))).Message);
        }

        using (var context = Context(_college))
        {
            await Staff(context).SendHallTicketsAsync(Request(), Site);
            var link = (await context.TaskLinks.SingleAsync(l => l.StdMstId == _student && l.Status == TaskLinkStatuses.Active)).Id;
            // Asha downloads; a resend to those who haven't reaches Ravi only.
            await HallTickets(context).MarkDownloadedAsync(Student(link));
            var again = await Staff(context).SendHallTicketsAsync(new HallTicketLinksRequest { Ayid = _ay, CourseId = _course, Semester = "Sem-1", Pattern = "NEP - R-24", ExamId = _exam, OnlyNotDownloaded = true }, Site);
            Assert.Equal(1, again.Queued);
            Assert.Equal(other, (await context.TaskLinks.SingleAsync(l => l.BatchId == again.BatchId)).StdMstId);
            Assert.Equal(1, (await Staff(context).PreviewHallTicketsAsync(Request())).Downloaded);
        }
    }

    [Theory]
    [InlineData("1234 5678 9012", "123456789012")]
    [InlineData("1234-5678-9012", "123456789012")]
    [InlineData("  ", null)]
    public void Abc_id_is_twelve_digits_with_spaces_and_dashes_dropped(string input, string? expected) =>
        Assert.Equal(expected, StudentMasterService.CleanAbcId(input));

    [Theory]
    [InlineData("12345678901")]
    [InlineData("12345678901A")]
    public void A_wrong_abc_id_is_refused(string input) =>
        Assert.Throws<InvalidOperationException>(() => StudentMasterService.CleanAbcId(input));

    [Fact]
    public async Task A_student_can_fill_in_their_abc_id_when_allowed_but_not_one_already_taken()
    {
        using var context = Context(_college);
        context.StudentMasters.Add(new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "2400002", FirstName = "Ravi", LastName = "Patil", AbcId = "999999999999", CollegeId = _college });
        var batch = new TaskBatch { Id = Guid.NewGuid(), CollegeId = _college, TaskTypeCode = TaskTypes.StudentMaster, Title = "d", Deadline = DateTime.UtcNow.AddDays(3), EditableFields = "abcId" };
        var linkId = Guid.NewGuid();
        context.TaskBatches.Add(batch);
        context.TaskLinks.Add(new TaskLink { Id = linkId, CollegeId = _college, TaskTypeCode = TaskTypes.StudentMaster, BatchId = batch.Id, StdMstId = _student, ExpiresAt = DateTime.UtcNow.AddDays(3) });
        await context.SaveChangesAsync();

        Assert.Contains("another student", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Details(context).SaveAsync(Student(linkId), new StudentDetailsUpdate { AbcId = "9999 9999 9999" }))).Message);
        var saved = await Details(context).SaveAsync(Student(linkId), new StudentDetailsUpdate { AbcId = "1111 2222 3333" });
        Assert.Equal(new[] { "ABC ID" }, saved.Changed);
        Assert.Equal("111122223333", (await context.StudentMasters.SingleAsync(s => s.StdMstId == _student)).AbcId);
    }
}
