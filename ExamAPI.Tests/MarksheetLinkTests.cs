using System.Security.Claims;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using ExamAPI.Services.GenerateHallTicket;
using ExamAPI.Services.Report;
using ExamAPI.Services.StudentAccess;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>
/// Marksheet links: staff email the students of a declared exam their marksheet link; the student sees the exam
/// and result date, downloads the same single-student PDF staff generate, and the first download is recorded.
/// </summary>
public sealed class MarksheetLinkTests
{
    private const string ServerKey = "test-server-key-that-is-at-least-32-chars";
    private const string Site = "https://www.vivacollege.in/gradesphereapp";
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly SecretProtector _protector = new(ServerKey);
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _course = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly Guid _exam = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();
    private static readonly DateTime ResultDate = new(2026, 11, 1);

    private ApplicationDbContext Context(Guid? college)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_db).Options;
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.CollegeId).Returns(college);
        return new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, user.Object);
    }

    public MarksheetLinkTests()
    {
        using var context = Context(_college);
        context.Database.EnsureCreated();
        context.Colleges.Add(new College { CollegeId = _college, Name = "VIVA", CollegeCode = "VIVA", CollegeCenter = "Virar", ContactEmail = "a@b.in", ContactPhone = "1" });
        context.CourseMasters.Add(new CourseMaster { CourseId = _course, Name = "Civil Engineering", CourseCode = "CIV", CollegeId = _college });
        context.Exams.Add(new ExamMaster { ExamId = _exam, Name = "DEC 2026", ExamType = "Regular", IsActive = true, CourseId = _course, CollegeId = _college });
        context.StudentMasters.Add(new StudentMaster { StdMstId = _student, StudentId = "2400001", FirstName = "Asha", LastName = "Patil", Email = "asha@viva.in", StudentPRN = "ERN-1", CollegeId = _college });
        context.MarksMasters.Add(new MarksMaster { MarksId = Guid.NewGuid(), StudentID = "2400001", StdMstId = _student, ExamId = _exam, SemesterId = "Sem-1", Pattern = "NEP - R-24", AcademicYearAYID = _ay, SeatNo = "1001", CollegeId = _college });
        context.EmailAccounts.Add(new EmailAccount
        {
            Id = Guid.NewGuid(), CollegeId = null, Host = "smtp-relay.gmail.com", Username = "gs@viva.in",
            PasswordProtected = _protector.Protect("app-password"), FromAddress = "noreply@viva.in",
        });
        context.SaveChanges();
    }

    private MarksheetLinksRequest Request() => new()
    {
        Ayid = _ay,
        CourseId = _course,
        Semester = "Sem-1",
        Pattern = "NEP - R-24",
        ExamId = _exam,
        Days = 30,
    };

    private TaskLinkService Staff(ApplicationDbContext context)
    {
        var resolver = new EmailAccountResolver(context, _protector);
        return new TaskLinkService(context, new EmailOutbox(context, resolver, new Mock<IEmailSender>().Object, NullLogger<EmailOutbox>.Instance), resolver, new Mock<IGenerateHallTicketService>().Object);
    }

    private static ExamAPI.Models.DeclareResult Declared(bool declared = true) => new()
    {
        DeclareID = Guid.NewGuid(),
        ExamId = Guid.Empty, // replaced in DeclareAsync
        CourseId = Guid.Empty,
        AcademicYear = Guid.Empty,
        Sem_id = "",
        Pattern = "",
        IsDeclare = declared,
        DeclareDate = declared ? ResultDate : null,
    };

    private void DeclareAsync(ApplicationDbContext context, bool declared = true)
    {
        var row = context.DeclareResults.FirstOrDefault();
        if (row == null)
        {
            row = Declared(declared);
            row.ExamId = _exam;
            row.CourseId = _course;
            row.AcademicYear = _ay;
            row.Sem_id = "Sem-1";
            row.Pattern = "NEP - R-24";
            context.DeclareResults.Add(row);
        }
        else
        {
            row.IsDeclare = declared;
            row.DeclareDate = declared ? ResultDate : null;
        }
        context.SaveChanges();
    }

    /// <summary>Sends the marksheet links (the result must be declared) and answers the student's link id.</summary>
    private async Task<Guid> SendAsync()
    {
        using (var context = Context(_college))
        {
            DeclareAsync(context);
        }
        using (var context = Context(_college))
        {
            await Staff(context).SendMarksheetsAsync(Request(), Site);
            return (await context.TaskLinks.SingleAsync(l => l.TaskTypeCode == TaskTypes.Marksheet && l.Status == TaskLinkStatuses.Active)).Id;
        }
    }

    private ClaimsPrincipal Student(Guid link) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, _student.ToString()),
        new Claim(ClaimTypes.Role, StudentAccessAuth.RoleName),
        new Claim("CollegeId", _college.ToString()),
        new Claim(StudentAccessAuth.LinkClaim, link.ToString()),
        new Claim(StudentAccessAuth.SessionClaim, Guid.NewGuid().ToString()),
        new Claim(StudentAccessAuth.TaskClaim, TaskTypes.Marksheet),
    }, StudentAccessAuth.Scheme));

    [Fact]
    public async Task The_marksheet_is_ready_only_after_declare_and_download_is_recorded_once()
    {
        var link = await SendAsync();
        using var context = Context(_college);
        var reports = new Mock<IReportService>();
        reports.Setup(r => r.GenerateMarksheetPdfAsync(
                _student, _exam, "Sem-1", "NEP - R-24", false, ResultDate, _college, false))
            .ReturnsAsync(new byte[] { 1, 2, 3 });
        var service = new StudentMarksheetService(context, reports.Object);

        // Withdrawn: the page explains, and the PDF is refused.
        DeclareAsync(context, declared: false);
        var notReady = await service.GetAsync(Student(link));
        Assert.False(notReady.Ready);
        Assert.Contains("hasn't been declared", notReady.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PdfAsync(Student(link)));

        // Declared: the page names the exam and the PDF comes from the same single-student generator.
        DeclareAsync(context);
        var ready = await service.GetAsync(Student(link));
        Assert.True(ready.Ready);
        Assert.Equal(("DEC 2026", "Sem-1", "2400001"), (ready.ExamName, ready.Semester, ready.StudentId));
        Assert.Equal(ResultDate, ready.ResultDate);

        var (bytes, name) = await service.PdfAsync(Student(link));
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        Assert.Contains("2400001", name);
        reports.Verify(r => r.GenerateMarksheetPdfAsync(
            _student, _exam, "Sem-1", "NEP - R-24", false, ResultDate, _college, false), Times.Once);
        Assert.NotNull((await context.TaskLinks.SingleAsync(l => l.Id == link)).UsedAt);

        // A second download is fine; staff still see one download and one audit row.
        await service.PdfAsync(Student(link));
        Assert.Equal(1, await context.AuditLogs.CountAsync(a => a.Event == "MarksheetDownloaded"));
    }

    [Fact]
    public async Task Preview_reports_declare_state_and_the_batch_is_download_only()
    {
        using var context = Context(_college);
        var staff = Staff(context);
        Assert.False((await staff.PreviewMarksheetsAsync(Request())).ResultDeclared);

        DeclareAsync(context);
        var preview = await staff.PreviewMarksheetsAsync(Request());
        Assert.True(preview.ResultDeclared);
        Assert.Equal((1, 1), (preview.Students, preview.Sendable));

        var result = await staff.SendMarksheetsAsync(Request(), Site);
        Assert.Equal(1, result.Queued);
        var batch = await context.TaskBatches.SingleAsync(b => b.Id == result.BatchId);
        Assert.Equal((TaskTypes.Marksheet, (Guid?)_exam), (batch.TaskTypeCode, batch.RefId));
        Assert.Equal("", batch.EditableFields);
    }

    [Fact]
    public async Task Undeclared_result_blocks_sending_and_stopping_the_links_takes_the_marksheet_back()
    {
        using (var empty = Context(_college))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Staff(empty).SendMarksheetsAsync(Request(), Site));
            Assert.Contains("Declare the result", error.Message);
        }

        var link = await SendAsync();
        using var context = Context(_college);
        var batch = (await context.TaskLinks.SingleAsync(l => l.Id == link)).BatchId!.Value;
        await Staff(context).StopBatchAsync(batch);
        var service = new StudentMarksheetService(context, new Mock<IReportService>().Object);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(Student(link)));
    }
}
