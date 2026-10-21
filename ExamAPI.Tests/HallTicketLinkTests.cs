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

    private async Task ReleaseAsync(bool released = true, DateTime? from = null)
    {
        using var context = Context(_college);
        context.DeclareResults.Add(new DeclareResult
        {
            DeclareID = Guid.NewGuid(), CollegeId = _college, ExamId = _exam, CourseId = _course, AcademicYear = _ay,
            Sem_id = "Sem-1", Pattern = "NEP - R-24", ReleaseHallTicket = released, HallTicketDeclareDate = from ?? DateTime.UtcNow.AddDays(-1),
        });
        await context.SaveChangesAsync();
    }

    private HallTicketLinksRequest Request() => new() { Ayid = _ay, CourseId = _course, Semester = "Sem-1", Pattern = "NEP - R-24", ExamId = _exam, Days = 30 };

    private TaskLinkService Staff(ApplicationDbContext context)
    {
        var resolver = new EmailAccountResolver(context, _protector);
        return new TaskLinkService(context, new EmailOutbox(context, resolver, new Mock<IEmailSender>().Object, NullLogger<EmailOutbox>.Instance), resolver);
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
    public async Task Links_go_only_for_a_released_exam_and_name_the_exam_without_stopping_a_details_link()
    {
        using (var context = Context(_college))
            Assert.Contains("Release the hall ticket", (await Assert.ThrowsAsync<InvalidOperationException>(() => Staff(context).SendHallTicketsAsync(Request(), Site))).Message);

        await ReleaseAsync();
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
        await ReleaseAsync();
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
    public async Task A_withdrawn_or_future_release_says_so_instead_of_showing_the_hall_ticket()
    {
        await ReleaseAsync();
        var link = await SendAsync();
        using (var context = Context(_college))
        {
            var row = await context.DeclareResults.SingleAsync();
            row.HallTicketDeclareDate = DateTime.UtcNow.AddDays(5);
            await context.SaveChangesAsync();
            Assert.Contains("will be available from", (await HallTickets(context).GetAsync(Student(link))).Message);

            row.ReleaseHallTicket = false;
            await context.SaveChangesAsync();
            var view = await HallTickets(context).GetAsync(Student(link));
            Assert.False(view.Ready);
            Assert.Contains("isn't available", view.Message);
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
