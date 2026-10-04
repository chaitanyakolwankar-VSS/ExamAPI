using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Dashboard;
using ExamAPI.Services.DeclareResult;
using ExamAPI.Services.ReleaseHallTicket;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// T-28: Declare Result / Release Hall Ticket list their exams from ExamMaster (LEFT-joined with
/// DeclareResult) so a fresh college needs no row; the row is created by the explicit save. The
/// semester comes from the request, MarksMaster.SemesterId and DeclareResult.Sem_id -- never from
/// ExamMaster.Semester, which nothing writes. The dashboard exam-type chart follows the same rule.
/// </summary>
public sealed class DeclareResultTests
{
    private const string Sem = "Sem-6";
    private const string Pattern = "NEP";

    private readonly ApplicationDbContext _context;
    private readonly DResultService _declare;
    private readonly ReleaseHallticketService _release;
    private readonly DashboardService _dashboard;
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly Guid _course = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();

    public DeclareResultTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        var repo = new GenericRepository(_context);
        _declare = new DResultService(_context, repo);
        _release = new ReleaseHallticketService(_context, repo);
        _dashboard = new DashboardService(_context);
    }

    // ---------------------------------------------------------------- seed helpers

    private ExamMaster Exam(string name, string type = "Regular", bool active = true, Guid? revalFor = null,
        Guid? college = null, string? semester = null)
    {
        var e = new ExamMaster
        {
            ExamId = Guid.NewGuid(),
            Name = name,
            ExamType = type,
            IsActive = active,
            RevaluationForExamId = revalFor,
            CourseId = _course,
            AcademicYearAYID = _ay,
            CollegeId = college ?? _college,
            Semester = semester, // always null in real data
        };
        _context.Exams.Add(e);
        return e;
    }

    private MarksMaster Student(ExamMaster exam, string semester = Sem, string pattern = Pattern,
        Guid? college = null, string? seat = null, int? mark = null, Guid? studentId = null)
    {
        var mm = new MarksMaster
        {
            MarksId = Guid.NewGuid(),
            ExamId = exam.ExamId,
            StdMstId = studentId ?? Guid.NewGuid(),
            SemesterId = semester,
            Pattern = pattern,
            SeatNo = seat,
            AcademicYearAYID = _ay,
            CollegeId = college ?? _college,
        };
        _context.MarksMasters.Add(mm);
        if (mark != null)
            _context.StudentMarks.Add(new StudentMarks { Id = Guid.NewGuid(), MarksId = mm.MarksId, Marks = mark });
        return mm;
    }

    /// <summary>What a bulk-marksheet run records; Declare Result requires it first.</summary>
    private async Task MarksheetsGenerated(ExamMaster exam, string semester = Sem)
    {
        await DResultService.RecordGenerationAsync(_context, exam, semester, Pattern, dr => dr.ResDeclare += 1);
        await _context.SaveChangesAsync();
    }

    private GetDeclareExam ListRequest(string semester = Sem) =>
        new() { CourseId = _course, Ayid = _ay, Semester = semester, Pattern = Pattern };

    private ToggleDeclareResultDTO DeclareRequest(Guid examId, bool declare, DateTime? date = null, string semester = Sem) =>
        new() { ExamId = examId, CourseId = _course, Ayid = _ay, Semester = semester, Pattern = Pattern, IsDeclare = declare, DeclareDate = date };

    private ToggleReleaseHallTicketDTO ReleaseRequest(Guid examId, bool release, DateTime? date = null, string semester = Sem) =>
        new() { ExamId = examId, CourseId = _course, Ayid = _ay, Semester = semester, Pattern = Pattern, ReleaseHallTicket = release, HallTicketDeclareDate = date };

    // ---------------------------------------------------------------- Declare Result list

    [Fact]
    public async Task Declare_lists_an_exam_that_has_no_DeclareResult_row_as_not_declared()
    {
        var exam = Exam("Regular Dec 2025"); // Semester stays null
        Student(exam);
        await _context.SaveChangesAsync();

        var list = await _declare.GetExam(ListRequest());

        var row = Assert.Single(list);
        Assert.Equal(exam.ExamId, row.ExamId);
        Assert.False(row.IsDeclare);
        Assert.Null(row.DeclareDate);
        Assert.False(row.HasRecord);
        Assert.Empty(_context.DeclareResults); // listing never writes

        var table = Assert.Single(await _declare.GetTableExam(new DeclareExamTable
            { CourseId = _course, Ayid = _ay, Semester = Sem, ExamId = exam.ExamId, Pattern = Pattern }));
        Assert.False(table.IsDeclare);
    }

    [Fact]
    public async Task Declare_list_excludes_inactive_other_semester_deleted_and_other_college_exams()
    {
        var ok = Exam("OK"); Student(ok);
        var inactive = Exam("Inactive", active: false); Student(inactive);
        var otherSem = Exam("Other semester"); Student(otherSem, semester: "Sem-5");
        var noStudents = Exam("No students");
        var otherPattern = Exam("Other pattern"); Student(otherPattern, pattern: "CBCS");
        var theirs = Exam("Theirs", college: _otherCollege); Student(theirs, college: _otherCollege);
        var deleted = Exam("Deleted"); Student(deleted);
        await _context.SaveChangesAsync();
        deleted.IsDeleted = true; // soft-delete after the insert (SaveChanges stamps new rows as live)
        await _context.SaveChangesAsync();

        var names = (await _declare.GetExam(ListRequest())).Select(e => e.Examname).ToList();

        Assert.Equal(new[] { "OK" }, names);
    }

    [Fact]
    public async Task Declare_list_labels_atkt_and_revaluation_exams()
    {
        var regular = Exam("Regular"); Student(regular);
        var atkt = Exam("KT", type: "A.T.K.T"); Student(atkt);
        var reval = Exam("Reval", revalFor: regular.ExamId); Student(reval);
        await _context.SaveChangesAsync();

        var names = (await _declare.GetExam(ListRequest())).Select(e => e.Examname).OrderBy(n => n).ToList();

        Assert.Equal(new[] { "KT (A.T.K.T)", "Regular", "Reval (Revaluation)" }, names);
    }

    // ---------------------------------------------------------------- Declare save

    [Fact]
    public async Task Declare_save_creates_the_row_with_the_college_stamped_then_updates_it()
    {
        var exam = Exam("Regular"); Student(exam);
        await _context.SaveChangesAsync();
        await MarksheetsGenerated(exam);
        var date = new DateTime(2026, 10, 3);

        Assert.True(await _declare.ToggleDeclare(DeclareRequest(exam.ExamId, true, date)));

        var created = Assert.Single(_context.DeclareResults);
        Assert.Equal(_college, created.CollegeId);
        Assert.Equal(Sem, created.Sem_id);
        Assert.Equal(Pattern, created.Pattern);
        Assert.True(created.IsDeclare);
        Assert.Equal(date, created.DeclareDate);

        var listed = Assert.Single(await _declare.GetExam(ListRequest()));
        Assert.True(listed.IsDeclare);
        Assert.True(listed.HasRecord);
        Assert.Equal(date, listed.DeclareDate);

        // Second save updates the same row (no duplicate), clearing the date on undeclare.
        Assert.True(await _declare.ToggleDeclare(DeclareRequest(exam.ExamId, false)));

        var updated = Assert.Single(_context.DeclareResults);
        Assert.Equal(created.DeclareID, updated.DeclareID);
        Assert.False(updated.IsDeclare);
        Assert.Null(updated.DeclareDate);
    }

    [Fact]
    public async Task Declare_save_needs_a_date_and_an_exam_the_screen_can_list()
    {
        var exam = Exam("Regular"); Student(exam);
        var elsewhere = Exam("Other college", college: _otherCollege); Student(elsewhere, college: _otherCollege);
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _declare.ToggleDeclare(DeclareRequest(exam.ExamId, true)));
        Assert.False(await _declare.ToggleDeclare(DeclareRequest(elsewhere.ExamId, true, DateTime.Today)));
        Assert.False(await _declare.ToggleDeclare(DeclareRequest(exam.ExamId, true, DateTime.Today, semester: "Sem-1"))); // no students there
        Assert.Empty(_context.DeclareResults);
    }

    [Fact]
    public async Task Locked_exams_stay_hidden_on_Declare_Result_but_not_on_Release()
    {
        var open = Exam("Open"); Student(open);
        var locked = Exam("Locked"); locked.IsLocked = true; Student(locked);
        await _context.SaveChangesAsync();
        await MarksheetsGenerated(locked);

        Assert.Equal("Open", Assert.Single(await _declare.GetExam(ListRequest())).Examname);
        Assert.Empty(await _declare.GetTableExam(new DeclareExamTable
            { CourseId = _course, Ayid = _ay, Semester = Sem, Pattern = Pattern, ExamId = locked.ExamId }));
        Assert.False(await _declare.ToggleDeclare(DeclareRequest(locked.ExamId, true, DateTime.Today)));
        Assert.False(Assert.Single(_context.DeclareResults).IsDeclare);

        Assert.Equal(2, (await _release.GetExam(ListRequest())).Count);
    }

    [Fact]
    public async Task Declare_is_refused_until_the_marksheets_are_generated()
    {
        var exam = Exam("Regular"); Student(exam);
        await _context.SaveChangesAsync();

        var before = Assert.Single(await _declare.GetExam(ListRequest()));
        Assert.False(before.MarksheetGenerated);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _declare.ToggleDeclare(DeclareRequest(exam.ExamId, true, DateTime.Today)));
        Assert.Empty(_context.DeclareResults);

        await MarksheetsGenerated(exam);
        var row = Assert.Single(_context.DeclareResults);
        Assert.Equal(_college, row.CollegeId);
        Assert.False(row.IsDeclare);
        Assert.True(Assert.Single(await _declare.GetExam(ListRequest())).MarksheetGenerated);

        Assert.True(await _declare.ToggleDeclare(DeclareRequest(exam.ExamId, true, DateTime.Today)));
        Assert.True(await _declare.ToggleDeclare(DeclareRequest(exam.ExamId, false)));
        Assert.Equal(row.DeclareID, Assert.Single(_context.DeclareResults).DeclareID);
    }

    [Fact]
    public async Task Marksheets_generated_for_one_semester_do_not_open_another()
    {
        var exam = Exam("Regular");
        Student(exam, semester: "Sem-5");
        Student(exam, semester: "Sem-6");
        await _context.SaveChangesAsync();
        await MarksheetsGenerated(exam, "Sem-5");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _declare.ToggleDeclare(DeclareRequest(exam.ExamId, true, DateTime.Today, semester: "Sem-6")));
        Assert.True(await _declare.ToggleDeclare(DeclareRequest(exam.ExamId, true, DateTime.Today, semester: "Sem-5")));
    }

    [Fact]
    public async Task Generation_counts_reuse_the_hall_ticket_row()
    {
        var exam = Exam("Regular"); Student(exam);
        await _context.SaveChangesAsync();

        await _release.ToggleReleaseHallTicket(ReleaseRequest(exam.ExamId, true, DateTime.Today));
        await MarksheetsGenerated(exam);
        await DResultService.RecordGenerationAsync(_context, exam, Sem, Pattern, dr => dr.GazetteGnrt += 1);
        await _context.SaveChangesAsync();

        var row = Assert.Single(_context.DeclareResults);
        Assert.True(row.ReleaseHallTicket);
        Assert.Equal(1, row.ResDeclare);
        Assert.Equal(1, row.GazetteGnrt);
        Assert.True(Assert.Single(await _declare.GetExam(ListRequest())).GazetteGenerated);
    }

    [Fact]
    public async Task Generating_the_gazette_records_it_for_the_dashboard()
    {
        _context.CourseMasters.Add(new CourseMaster { CourseId = _course, Name = "B.Pharm", CourseCode = "BPH", CollegeId = _college });
        var exam = Exam("Regular"); Student(exam);
        await _context.SaveChangesAsync();
        var reports = new ExamAPI.Services.Report.ReportService(new Mock<ExamAPI.Services.Result.IResultService>().Object, _context);

        await reports.GenerateGazetteExcelAsync(new GazetteRequestDto { ExamId = exam.ExamId, SemId = Sem, Pattern = Pattern }, _college);

        var row = Assert.Single(_context.DeclareResults);
        Assert.Equal(1, row.GazetteGnrt);
        Assert.NotNull(row.GazetteDate);
        Assert.Equal(0, row.ResDeclare);
        Assert.Equal(1, Assert.Single(await _dashboard.GetExamLifecycleAsync(_college, _ay)).GazetteGnrt);
    }

    // ---------------------------------------------------------------- semester filter

    [Fact]
    public async Task Semester_filter_uses_DeclareResult_Sem_id_and_the_request_never_ExamMaster_Semester()
    {
        // ExamMaster.Semester says Sem-1 but nothing is ever written there; only the row's Sem_id counts.
        var exam = Exam("Regular", semester: "Sem-1");
        _context.DeclareResults.Add(new DeclareResult
        {
            DeclareID = Guid.NewGuid(), CollegeId = _college, ExamId = exam.ExamId, CourseId = _course,
            AcademicYear = _ay, Sem_id = "Sem-5", Pattern = Pattern, IsDeclare = true, DeclareDate = new DateTime(2026, 1, 1)
        });
        await _context.SaveChangesAsync();

        Assert.Single(await _declare.GetExam(ListRequest("Sem-5")));
        Assert.Empty(await _declare.GetExam(ListRequest("Sem-1")));
        Assert.Empty(await _declare.GetExam(ListRequest("Sem-6")));
        Assert.True((await _declare.GetExam(ListRequest("Sem-5")))[0].IsDeclare);
    }

    [Fact]
    public async Task Declaring_one_semester_does_not_mark_the_exam_declared_for_another()
    {
        var exam = Exam("Regular");
        Student(exam, semester: "Sem-5");
        Student(exam, semester: "Sem-6");
        await _context.SaveChangesAsync();
        await MarksheetsGenerated(exam, "Sem-5");

        await _declare.ToggleDeclare(DeclareRequest(exam.ExamId, true, DateTime.Today, semester: "Sem-5"));

        Assert.True((await _declare.GetExam(ListRequest("Sem-5")))[0].IsDeclare);
        Assert.False((await _declare.GetExam(ListRequest("Sem-6")))[0].IsDeclare);
    }

    // ---------------------------------------------------------------- Release Hall Ticket

    [Fact]
    public async Task Release_list_shows_not_released_without_a_row_and_skips_revaluation_exams()
    {
        var regular = Exam("Regular"); Student(regular);
        var reval = Exam("Reval", revalFor: regular.ExamId); Student(reval);
        await _context.SaveChangesAsync();

        var row = Assert.Single(await _release.GetExam(ListRequest()));

        Assert.Equal(regular.ExamId, row.ExamId);
        Assert.False(row.ReleaseHallTicket);
        Assert.False(row.HasRecord);
        Assert.Empty(_context.DeclareResults);
    }

    [Fact]
    public async Task Release_flow_creates_the_row_releases_and_revokes()
    {
        var exam = Exam("Regular"); Student(exam);
        await _context.SaveChangesAsync();
        var date = new DateTime(2026, 11, 2);

        await Assert.ThrowsAsync<ArgumentException>(() => _release.ToggleReleaseHallTicket(ReleaseRequest(exam.ExamId, true)));
        Assert.True(await _release.ToggleReleaseHallTicket(ReleaseRequest(exam.ExamId, true, date)));

        var row = Assert.Single(_context.DeclareResults);
        Assert.Equal(_college, row.CollegeId);
        Assert.True(row.ReleaseHallTicket);
        Assert.Equal(date, row.HallTicketDeclareDate);
        Assert.NotNull(row.HallTicketUpdatedAt);
        Assert.False(row.IsDeclare); // releasing a hall ticket does not declare the result

        var listed = Assert.Single(await _release.GetTableExam(new DeclareExamTable
            { CourseId = _course, Ayid = _ay, Semester = Sem, ExamId = exam.ExamId, Pattern = Pattern }));
        Assert.True(listed.ReleaseHallTicket);
        Assert.Equal(date, listed.HallTicketDeclareDate);

        Assert.True(await _release.ToggleReleaseHallTicket(ReleaseRequest(exam.ExamId, false)));
        var revoked = Assert.Single(_context.DeclareResults);
        Assert.Equal(row.DeclareID, revoked.DeclareID);
        Assert.False(revoked.ReleaseHallTicket);
        Assert.Null(revoked.HallTicketDeclareDate);
    }

    [Fact]
    public async Task Release_and_declare_share_one_row_per_exam_and_semester()
    {
        var exam = Exam("Regular"); Student(exam);
        await _context.SaveChangesAsync();

        await _release.ToggleReleaseHallTicket(ReleaseRequest(exam.ExamId, true, DateTime.Today));
        await MarksheetsGenerated(exam);
        await _declare.ToggleDeclare(DeclareRequest(exam.ExamId, true, DateTime.Today));

        var row = Assert.Single(_context.DeclareResults);
        Assert.True(row.ReleaseHallTicket);
        Assert.True(row.IsDeclare);
    }

    [Fact]
    public async Task Release_rejects_an_exam_the_screen_cannot_list()
    {
        var reval = Exam("Reval", revalFor: Guid.NewGuid()); Student(reval);
        await _context.SaveChangesAsync();

        Assert.False(await _release.ToggleReleaseHallTicket(ReleaseRequest(reval.ExamId, true, DateTime.Today)));
        Assert.Empty(_context.DeclareResults);
    }

    // ---------------------------------------------------------------- dashboard

    [Fact]
    public async Task Dashboard_exam_type_count_works_for_exams_whose_Semester_is_null()
    {
        var regular = Exam("Regular");
        var atkt = Exam("KT", type: "A.T.K.T");
        var theirs = Exam("Theirs", college: _otherCollege);
        var stdA = Guid.NewGuid();
        Student(regular, studentId: stdA);
        Student(regular, studentId: Guid.NewGuid());
        Student(regular, semester: "Sem-5", studentId: stdA);
        Student(atkt, studentId: stdA);
        Student(theirs, college: _otherCollege);
        await _context.SaveChangesAsync();

        var result = await _dashboard.GetSemesterWiseExamTypeCountAsync(_course, _ay);

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.Single(r => r.SemesterId == Sem && r.ExamType == "Regular").StudentCount);
        Assert.Equal(1, result.Single(r => r.SemesterId == "Sem-5" && r.ExamType == "Regular").StudentCount);
        Assert.Equal(1, result.Single(r => r.SemesterId == Sem && r.ExamType == "A.T.K.T").StudentCount);
    }

    [Fact]
    public async Task Dashboard_lifecycle_shows_an_exam_with_no_DeclareResult_row_then_follows_release_and_declare()
    {
        var exam = Exam("Regular");
        Student(exam, seat: "S1", mark: 40);
        Student(exam, seat: "S2");
        Student(exam);
        var theirs = Exam("Theirs", college: _otherCollege); Student(theirs, college: _otherCollege, seat: "X", mark: 1);
        await _context.SaveChangesAsync();

        var before = Assert.Single(await _dashboard.GetExamLifecycleAsync(_college, _ay));
        Assert.Equal("Regular", before.ExamName);
        Assert.Equal(3, before.AssignedStudent);
        Assert.Equal(2, before.SeatNo);
        Assert.Equal(1, before.MarksEntered);
        Assert.False(before.ReleaseHallTicket);
        Assert.False(before.IsDeclare);

        await _release.ToggleReleaseHallTicket(ReleaseRequest(exam.ExamId, true, DateTime.Today));
        await MarksheetsGenerated(exam);
        await _declare.ToggleDeclare(DeclareRequest(exam.ExamId, true, DateTime.Today));

        var after =Assert.Single(await _dashboard.GetExamLifecycleAsync(_college, _ay));
        Assert.True(after.ReleaseHallTicket);
        Assert.True(after.IsDeclare);

        // A caller cannot read another college's lifecycle by passing its id: the tenant filter wins.
        Assert.Empty(await _dashboard.GetExamLifecycleAsync(_otherCollege, _ay));
    }

    [Fact]
    public async Task Dashboard_stats_runs_every_query_on_the_one_context()
    {
        var exam = Exam("Regular"); Student(exam, seat: "S1", mark: 40);
        await _context.SaveChangesAsync();

        var stats = await _dashboard.GetDashboardStatsAsync(_college, _ay);

        Assert.Equal(1, stats.TotalExamsConducted);
        Assert.Single(stats.ExamLifecycle);
    }
}
