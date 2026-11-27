using System.Security.Claims;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.AtktRevalExam;
using ExamAPI.Services.Common;
using ExamAPI.Services.Email;
using ExamAPI.Services.ExamApplications;
using ExamAPI.Services.GenerateHallTicket;
using ExamAPI.Services.Payments;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.Result.Engine.FactProviders;
using ExamAPI.Services.StudentAccess;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>
/// Exam applications end to end (docs/STUDENT_EXAM_APPLICATIONS.md, step 4): the student's link offers only what the ATKT
/// rules allow, the fee is the server's, and the gateway's confirmation puts the student into the exam — from a context
/// where nobody is signed in (the gateway's return), through the same assignment as the ATKT/Reval Exam screen.
/// </summary>
public class ExamApplicationFlowTests : IDisposable
{
    private const string Semester = "Sem-6";
    private const string Pattern = "NEP";

    /// <summary>The college a call is made as: staff or student session = the college; gateway return/background = null.</summary>
    private Guid? _signedIn;

    private sealed class TestUser : ICurrentUser
    {
        private readonly TenantScope _scope;
        private readonly Func<Guid?> _signedIn;
        public TestUser(TenantScope scope, Func<Guid?> signedIn) { _scope = scope; _signedIn = signedIn; }
        public Guid? CollegeId => _scope.CollegeId ?? _signedIn();
        public Guid? UserId => null;
        public bool IsPlatformAdmin => false;
    }

    private readonly ServiceProvider _root;
    private readonly TestPaymentGateway _gateway = new();

    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _ayid = Guid.NewGuid();
    private readonly Guid _course = Guid.NewGuid();
    private readonly Guid _patternId = Guid.NewGuid();
    private readonly Guid _source = Guid.NewGuid();
    private readonly Guid _target = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();
    private readonly Guid _link = Guid.NewGuid();
    private Guid _failed, _absent, _passed;

    public ExamApplicationFlowTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var accessor = new Mock<IHttpContextAccessor>().Object;
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Development");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<TenantScope>();
        services.AddScoped<ICurrentUser>(sp => new TestUser(sp.GetRequiredService<TenantScope>(), () => _signedIn));
        services.AddScoped(sp => new ApplicationDbContext(options, accessor, sp.GetRequiredService<ICurrentUser>()));
        services.AddScoped<IAtktRevalExamService>(sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            return new AtktRevalExamService(db, new GenericRepository(db),
                new EngineRegistry(new IFactProvider[] { new FailedSubjectCountProvider() }, Array.Empty<IActionHandler>()));
        });
        services.AddSingleton<ISecretProtector>(new SecretProtector("unit-test-server-key-0123456789-abcdefghij"));
        services.AddSingleton(env.Object);
        services.AddSingleton<IPaymentGateway>(_gateway);
        services.AddScoped<PaymentAccountResolver>();
        services.AddScoped<IPaymentAccountResolver>(sp => sp.GetRequiredService<PaymentAccountResolver>());
        services.AddScoped<IExamApplicationAssigner, ExamApplicationAssigner>();
        services.AddScoped<IPaymentService>(sp => new PaymentService(sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<IPaymentAccountResolver>(), null, null, sp.GetRequiredService<IExamApplicationAssigner>()));
        services.AddScoped<IStudentExamApplicationService, StudentExamApplicationService>();
        services.AddScoped<IExamApplicationAdminService, ExamApplicationAdminService>();
        _root = services.BuildServiceProvider();
        Seed();
    }

    public void Dispose() => _root.Dispose();

    private T Run<T>(Guid? asCollege, Func<IServiceProvider, Task<T>> action)
    {
        _signedIn = asCollege;
        using var scope = _root.CreateScope();
        return action(scope.ServiceProvider).GetAwaiter().GetResult();
    }

    private ApplicationDbContext Db(IServiceProvider sp) => sp.GetRequiredService<ApplicationDbContext>();

    private ClaimsPrincipal Student() => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, _student.ToString()),
        new Claim("CollegeId", _college.ToString()),
        new Claim(StudentAccessAuth.LinkClaim, _link.ToString()),
    }, "test"));

    private void Seed()
    {
        Run<int>(null, async sp =>
        {
            var db = Db(sp);
            db.Colleges.Add(new College { CollegeId = _college, Name = "VIVA", CollegeCode = "V", CollegeCenter = "Virar", ContactEmail = "a@a.in", ContactPhone = "1" });
            db.AcademicYears.Add(new AcademicYear { AYID = _ayid, CollegeId = _college, ShortDuration = "2025-26", FullDuration = "2025-2026" });
            db.CourseMasters.Add(new CourseMaster { CourseId = _course, Name = "Computer", CourseCode = "CS", CollegeId = _college });
            db.PatternMasters.Add(new PatternMaster { PatternId = _patternId, PatternName = Pattern, CollegeId = _college });
            db.Exams.Add(new ExamMaster { ExamId = _source, Name = "May 2025", ExamType = "Regular", CourseId = _course, AcademicYearAYID = _ayid, IsActive = true, CollegeId = _college });
            db.Exams.Add(new ExamMaster { ExamId = _target, Name = "Dec 2025", ExamType = "A.T.K.T", CourseId = _course, AcademicYearAYID = _ayid, IsActive = true, CollegeId = _college });
            db.StudentMasters.Add(new StudentMaster { StdMstId = _student, StudentId = "ST001", FirstName = "Asha", LastName = "Rao", Email = "asha@demo.invalid", Category = "OPEN", IsActive = true, CollegeId = _college });
            db.StudentEligibilities.Add(new StudentEligibility { Id = Guid.NewGuid(), StdMstId = _student, StudentId = "ST001", CourseId = _course, AYID = _ayid, SemesterId = Semester, Pattern = Pattern, CollegeId = _college });
            var marks = new MarksMaster { MarksId = Guid.NewGuid(), StdMstId = _student, StudentID = "ST001", ExamId = _source, AcademicYearAYID = _ayid, SemesterId = Semester, Pattern = Pattern, SeatNo = "A-101", CollegeId = _college, CreatedAt = DateTime.UtcNow.AddDays(-30) };
            db.MarksMasters.Add(marks);
            _failed = Subject(db, marks, "SUB1", "Maths", 20, 15, false);    // ESE 20/80 < 32: failed
            _absent = Subject(db, marks, "SUB2", "Physics", 0, 0, true);     // absent
            _passed = Subject(db, marks, "SUB3", "Chemistry", 60, 18, false); // passed

            db.ExamFeeSetups.Add(new ExamFeeSetup { Id = Guid.NewGuid(), CollegeId = _college, ExamId = _target, Kind = ExamApplicationKinds.Atkt, ClosesAt = DateTime.UtcNow.AddDays(10), PerSubjectFee = 300, FixedFee = 60, FixedFeeLabel = "Statement of marks" });
            db.PaymentAccounts.Add(new PaymentAccount { Id = Guid.NewGuid(), CollegeId = null, Provider = PaymentProviders.Test, IsTestMode = true, IsEnabled = true, UpdatedAt = DateTime.UtcNow });
            var batch = new TaskBatch { Id = Guid.NewGuid(), CollegeId = _college, TaskTypeCode = TaskTypes.AtktPayment, Title = "t", Deadline = DateTime.UtcNow.AddDays(10), RefId = _target, CourseId = _course, AYID = _ayid, SemesterId = Semester, Pattern = Pattern, LinkBaseUrl = "https://site.test/app" };
            db.TaskBatches.Add(batch);
            db.TaskLinks.Add(new TaskLink { Id = _link, CollegeId = _college, TaskTypeCode = TaskTypes.AtktPayment, BatchId = batch.Id, StdMstId = _student, RefId = _target, Status = TaskLinkStatuses.Active, ExpiresAt = DateTime.UtcNow.AddDays(10) });
            await db.SaveChangesAsync();
            return 0;
        });
    }

    private Guid Subject(ApplicationDbContext db, MarksMaster marks, string code, string name, int ese, int ia, bool absent)
    {
        var subjectId = Guid.NewGuid();
        var creditsId = Guid.NewGuid();
        db.SubjectMasters.Add(new SubjectMaster { SubjectId = subjectId, SubjectCode = code, Name = name, SemId = Semester, Pattern = Pattern, CourseId = _course, CollegeId = _college });
        db.SubjectCreditMasters.Add(new SubjectCreditMaster
        {
            CreditsId = creditsId, SubjectId = subjectId, TotalCredits = "4", AYID = _ayid.ToString(), PassingStrategy = PassingStrategies.HeadWise, CollegeId = _college,
            Credits = new List<SubjectCredits>
            {
                new() { Id = Guid.NewGuid(), Head = "H1", HeadType = "ESE", HeadOutOf = "80", HeadPass = "32", CreditsId = creditsId },
                new() { Id = Guid.NewGuid(), Head = "H2", HeadType = "IA", HeadOutOf = "20", HeadPass = "8", CreditsId = creditsId },
            },
        });
        db.StudentMarks.Add(new StudentMarks { Id = Guid.NewGuid(), MarksId = marks.MarksId, SubjectId = subjectId, CreditsId = creditsId, Head = "H1", Marks = ese, RawMarks = ese, IsAbsent = absent });
        db.StudentMarks.Add(new StudentMarks { Id = Guid.NewGuid(), MarksId = marks.MarksId, SubjectId = subjectId, CreditsId = creditsId, Head = "H2", Marks = ia, RawMarks = ia, IsAbsent = absent });
        return subjectId;
    }

    private StudentPayResult Pay(params Guid[] subjects) =>
        Run(_college, sp => sp.GetRequiredService<IStudentExamApplicationService>().PayAsync(Student(), subjects.ToList(), true, "https://api.test/", "https://site.test/app"));

    private PaymentOrder GatewayReturn(string orderNo) =>
        Run<PaymentOrder>(null, sp => sp.GetRequiredService<IPaymentService>().ConfirmAsync(orderNo));

    [Fact]
    public void The_student_applies_for_what_the_rules_allow_pays_and_is_put_into_the_exam()
    {
        var view = Run(_college, sp => sp.GetRequiredService<IStudentExamApplicationService>().GetAsync(Student()));
        Assert.Null(view.Message);
        Assert.True(view.IsOpen);
        Assert.True(view.Subjects.Single(s => s.SubjectId == _failed).Suggested);
        Assert.True(view.Subjects.Single(s => s.SubjectId == _absent).Selectable);
        Assert.False(view.Subjects.Single(s => s.SubjectId == _passed).Selectable);

        var quote = Run(_college, sp => sp.GetRequiredService<IStudentExamApplicationService>().QuoteAsync(Student(), new List<Guid> { _failed }));
        Assert.Equal(360, quote.Total);

        // A cleared subject is refused, whatever the browser sends.
        Assert.Throws<InvalidOperationException>(() => Pay(_failed, _passed));

        var pay = Pay(_failed, _absent);
        Assert.False(pay.Paid);
        Assert.Equal(660, pay.Amount);
        Assert.NotNull(pay.Redirect);

        _gateway.Decide(pay.OrderNo!, TestPaymentGateway.Outcome.Success);
        var order = GatewayReturn(pay.OrderNo!);
        Assert.Equal(PaymentOrderStatuses.Success, order.Status);

        // Assigned through the ATKT assignment: a marks row in the target exam, the two subjects fresh, the third carried.
        Run(null, async sp =>
        {
            var db = Db(sp);
            var app = await db.ExamApplications.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(ExamApplicationStatuses.Assigned, app.Status);
            Assert.Null(app.AssignError);
            var target = await db.MarksMasters.IgnoreQueryFilters().Include(m => m.StudentMarks).SingleAsync(m => m.ExamId == _target);
            Assert.Equal("A-101", target.SeatNo);
            Assert.All(target.StudentMarks!.Where(h => h.SubjectId == _failed || h.SubjectId == _absent), h => Assert.False(h.IsCarryForward));
            Assert.All(target.StudentMarks!.Where(h => h.SubjectId == _passed), h => Assert.True(h.IsCarryForward));
            Assert.NotNull((await db.TaskLinks.IgnoreQueryFilters().SingleAsync()).UsedAt);
            return 0;
        });

        // The student's page after the gateway, and the receipt.
        var after = Run(_college, sp => sp.GetRequiredService<IStudentExamApplicationService>().PaymentResultAsync(Student()));
        Assert.Equal(ExamApplicationStatuses.Assigned, after.Application!.Status);
        Assert.Equal("AT/2025-26/00001", after.Application.ReceiptNo);
        var receipt = Run(_college, sp => sp.GetRequiredService<IStudentExamApplicationService>().DocumentAsync(Student(), ExamDocumentParts.Receipt));
        var form = Run(_college, sp => sp.GetRequiredService<IStudentExamApplicationService>().DocumentAsync(Student(), ExamDocumentParts.Form));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(form.Pdf, 0, 4));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(receipt.Pdf, 0, 4));

        // Staff see it, and the fee report counts it once.
        var list = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().ListAsync(_target));
        var row = Assert.Single(list.Rows);
        Assert.Equal(ExamApplicationStatuses.Assigned, row.Status);
        Assert.Equal(660, list.Collected);
        var report = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().ReportAsync(new FeeReportFilter()));
        Assert.Equal(660, report.Total);
        Assert.Equal(660, report.Online);
    }

    [Fact]
    public void A_failed_assignment_is_shown_to_staff_and_can_be_retried()
    {
        Run(_college, async sp =>
        {
            var db = Db(sp);
            (await db.Exams.SingleAsync(e => e.ExamId == _target)).IsLocked = true;
            await db.SaveChangesAsync();
            return 0;
        });
        var pay = Pay(_failed);
        _gateway.Decide(pay.OrderNo!, TestPaymentGateway.Outcome.Success);
        GatewayReturn(pay.OrderNo!);

        var list = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().ListAsync(_target));
        var row = Assert.Single(list.Rows);
        Assert.Equal(ExamApplicationStatuses.AssignFailed, row.Status);
        Assert.Contains("locked", row.AssignError);
        Assert.Equal(1, list.NeedAttention);

        Run(_college, async sp =>
        {
            var db = Db(sp);
            (await db.Exams.SingleAsync(e => e.ExamId == _target)).IsLocked = false;
            await db.SaveChangesAsync();
            return 0;
        });
        var retried = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().RetryAssignAsync(row.ApplicationId!.Value));
        Assert.Equal(ExamApplicationStatuses.Assigned, retried.Status);
    }

    [Fact]
    public void An_unpaid_application_can_be_paid_at_the_office_or_cancelled_and_another_college_sees_nothing()
    {
        var pay = Pay(_failed); // the student chose, then left the gateway
        var appId = Run(null, async sp => (await Db(sp).ExamApplications.IgnoreQueryFilters().SingleAsync()).Id);

        Assert.Throws<KeyNotFoundException>(() => Run(Guid.NewGuid(), sp => sp.GetRequiredService<IExamApplicationAdminService>().PayAtOfficeAsync(appId, "R-1")));

        var row = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().PayAtOfficeAsync(appId, "Office receipt 77"));
        Assert.Equal(ExamApplicationStatuses.Assigned, row.Status);
        Assert.Equal(PaidVia.Office, row.PaidVia);
        Assert.Equal(360, row.Amount);
        Assert.Throws<InvalidOperationException>(() => Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().CancelAsync(appId, "x")));

        // The student's earlier, never-finished gateway order: if it now succeeds too, it is flagged, not counted.
        _gateway.Decide(pay.OrderNo!, TestPaymentGateway.Outcome.Success);
        GatewayReturn(pay.OrderNo!);
        var flagged = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().ListAsync(_target)).Rows.Single();
        Assert.True(flagged.PaidTwice);
        var done = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().RefundDoneAsync(appId, "Refunded at gateway 28 Nov"));
        Assert.False(done.PaidTwice);
    }

    [Fact]
    public void Staff_preview_lists_the_students_who_may_apply()
    {
        var preview = Run(_college, async sp =>
        {
            var db = Db(sp);
            var resolver = new EmailAccountResolver(db, sp.GetRequiredService<ISecretProtector>());
            var links = new TaskLinkService(db, new EmailOutbox(db, resolver, new Mock<IEmailSender>().Object, NullLogger<EmailOutbox>.Instance),
                resolver, new GenerateHallTicketService(db), sp.GetRequiredService<IAtktRevalExamService>());
            return await links.PreviewExamApplicationsAsync(new ExamApplicationLinksRequest { Ayid = _ayid, CourseId = _course, Semester = Semester, Pattern = Pattern, ExamId = _target });
        });
        Assert.Equal(1, preview.Students);
        Assert.Equal(1, preview.Sendable);
        Assert.True(preview.FeesSet);
    }
    [Fact]
    public void Paying_needs_the_declaration()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Run(_college, sp => sp.GetRequiredService<IStudentExamApplicationService>().PayAsync(Student(), new List<Guid> { _failed }, false, "https://api.test/", "https://site.test/app")));
        Assert.Contains("declaration", error.Message);
        var view = Run(_college, sp => sp.GetRequiredService<IStudentExamApplicationService>().GetAsync(Student()));
        Assert.Equal(ExamFormWording.DefaultDeclaration, view.Declaration);
        Assert.Equal(ExamFormWording.DefaultNote, view.FormNote);
    }

    private DTOs.AtktMatrixRequest Filter() => new()
    {
        CourseId = _course, Ayid = _ayid, Semester = Semester, Pattern = Pattern, Mode = AssignmentModes.Atkt, TargetExamId = _target,
    };

    [Fact]
    public void A_student_registered_on_the_ATKT_screen_shows_and_can_have_the_office_fee_recorded_without_changing_the_registration()
    {
        // Staff register the student directly, re-sitting only the ESE part of Maths.
        var saved = Run(_college, sp => sp.GetRequiredService<IAtktRevalExamService>().SaveAsync(new DTOs.AtktSaveRequest
        {
            Filter = Filter(),
            Students = new() { new() { StdMstId = _student, Subjects = new() { new() { SubjectId = _failed, Heads = new() { "H1" } } } } },
        }));
        Assert.True(saved.Success, saved.Message);

        var row = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().ListAsync(_target)).Rows.Single();
        Assert.True(row.InExam);
        Assert.True(row.RegisteredByStaff);
        Assert.Null(row.Status);

        var paid = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().RecordOfficePaymentAsync(_target, _student, "Office 12"));
        Assert.Equal(ExamApplicationStatuses.Assigned, paid.Status);
        Assert.Equal(PaidVia.Office, paid.PaidVia);
        Assert.Equal(360, paid.Amount);
        Assert.False(Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().ListAsync(_target)).Rows.Single().RegisteredByStaff);

        // The registration is untouched: only H1 of Maths is fresh.
        Run(null, async sp =>
        {
            var heads = await Db(sp).StudentMarks.IgnoreQueryFilters().Include(h => h.MarksMaster)
                .Where(h => h.MarksMaster!.ExamId == _target && h.SubjectId == _failed).ToListAsync();
            Assert.False(heads.Single(h => h.Head == "H1").IsCarryForward);
            Assert.True(heads.Single(h => h.Head == "H2").IsCarryForward);
            return 0;
        });
        var status = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().FeeStatusAsync(_target));
        Assert.Equal(PaidVia.Office, Assert.Single(status).PaidVia);
        var docs = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().ReceiptAsync(paid.ApplicationId!.Value));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(docs.Pdf, 0, 4));
    }

    [Fact]
    public void A_paid_student_removed_on_the_ATKT_screen_is_flagged()
    {
        var pay = Pay(_failed);
        _gateway.Decide(pay.OrderNo!, TestPaymentGateway.Outcome.Success);
        GatewayReturn(pay.OrderNo!);
        var removed = Run(_college, sp => sp.GetRequiredService<IAtktRevalExamService>().DeleteAssignmentAsync(new DTOs.AtktDeleteRequest { Filter = Filter(), StdMstId = _student }));
        Assert.True(removed.Success, removed.Message);

        var list = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().ListAsync(_target));
        var row = Assert.Single(list.Rows);
        Assert.True(row.NotInExam);
        Assert.Equal(1, list.NeedAttention);

        var again = Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().RetryAssignAsync(row.ApplicationId!.Value));
        Assert.Equal(ExamApplicationStatuses.Assigned, again.Status);
        Assert.False(Run(_college, sp => sp.GetRequiredService<IExamApplicationAdminService>().ListAsync(_target)).Rows.Single().NotInExam);
    }

    [Fact]
    public void The_college_can_word_the_form_itself()
    {
        var saved = Run(_college, sp => new ExamFeeService(Db(sp)).SaveFormWordingAsync(new ExamFormWordingInput { Declaration = "Our own words.", NoNote = true }));
        Assert.Equal("Our own words.", saved.Declaration);
        Assert.Null(saved.Note);
        Assert.False(saved.DeclarationIsDefault);
        var back = Run(_college, sp => new ExamFeeService(Db(sp)).SaveFormWordingAsync(new ExamFormWordingInput()));
        Assert.True(back.DeclarationIsDefault);
        Assert.Equal(ExamFormWording.DefaultNote, back.Note);
    }

    [Theory]
    [InlineData(360, "Rupees Three Hundred Sixty Only")]
    [InlineData(125050, "Rupees One Lakh Twenty Five Thousand Fifty Only")]
    [InlineData(0, "Rupees Zero Only")]
    public void Amounts_are_written_in_words_the_Indian_way(int amount, string words) =>
        Assert.Equal(words, IndianWords.Rupees(amount));
}
