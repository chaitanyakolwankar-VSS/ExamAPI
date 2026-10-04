using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Lookup;
using ExamAPI.Services.Result;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// T-19 layer A: /api/Lookup/bootstrap, /exams?purpose=, /subjects, and the T-25 fix to the
/// Apply Grace Marks exam list. Tenant isolation comes from the global query filter, so the
/// "other" college's rows are seeded with an explicit CollegeId and must never come back.
/// </summary>
public sealed class LookupServiceTests
{
    private readonly ApplicationDbContext _context;
    private readonly LookupService _service;
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly Guid _course = Guid.NewGuid();
    private readonly Guid _otherCourse = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly Guid _otherAy = Guid.NewGuid();

    public LookupServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new LookupService(_context, currentUser.Object);
    }

    // ---------------------------------------------------------------- seed helpers

    private ExamMaster Exam(string name, string type = "Regular", bool active = true, Guid? revalFor = null,
        Guid? course = null, Guid? ay = null, Guid? college = null, bool locked = false, string? semester = null)
    {
        var e = new ExamMaster
        {
            ExamId = Guid.NewGuid(),
            Name = name,
            ExamType = type,
            IsActive = active,
            RevaluationForExamId = revalFor,
            CourseId = course ?? _course,
            AcademicYearAYID = ay ?? _ay,
            CollegeId = college ?? _college,
            IsLocked = locked,
            Semester = semester,
        };
        _context.Exams.Add(e);
        return e;
    }

    private void Marks(ExamMaster exam, string semester, Guid? ay = null, Guid? college = null) =>
        _context.MarksMasters.Add(new MarksMaster
        {
            MarksId = Guid.NewGuid(),
            ExamId = exam.ExamId,
            SemesterId = semester,
            AcademicYearAYID = ay ?? _ay,
            CollegeId = college ?? _college,
        });

    private async Task<List<string>> Names(string purpose, string? semester = null)
    {
        var list = await _service.GetExamsAsync(_course, _ay, purpose, semester);
        Assert.NotNull(list);
        return list!.Select(e => e.Name).ToList();
    }

    // ---------------------------------------------------------------- bootstrap

    [Fact]
    public async Task Bootstrap_returns_own_college_data_in_contract_order()
    {
        _context.AcademicYears.AddRange(
            new AcademicYear { AYID = Guid.NewGuid(), FullDuration = "2023-2024", ShortDuration = "23-24", CollegeId = _college },
            new AcademicYear { AYID = Guid.NewGuid(), FullDuration = "2025-2026", ShortDuration = "25-26", IsCurrent = true, CollegeId = _college },
            new AcademicYear { AYID = Guid.NewGuid(), FullDuration = "2024-2025", ShortDuration = "24-25", CollegeId = _college },
            new AcademicYear { AYID = Guid.NewGuid(), FullDuration = "2030-2031", ShortDuration = "30-31", CollegeId = _otherCollege });
        _context.CourseMasters.AddRange(
            new CourseMaster { CourseId = Guid.NewGuid(), Name = "Pharmacy", CourseCode = "PHM", CollegeId = _college },
            new CourseMaster { CourseId = Guid.NewGuid(), Name = "Arts", CourseCode = "ART", CollegeId = _college },
            new CourseMaster { CourseId = Guid.NewGuid(), Name = "Aaa Other", CourseCode = "OTH", CollegeId = _otherCollege });
        _context.PatternMasters.AddRange(
            new PatternMaster { PatternId = Guid.NewGuid(), PatternName = "NEP", CollegeId = _college },
            new PatternMaster { PatternId = Guid.NewGuid(), PatternName = "CBCS", CollegeId = _college },
            new PatternMaster { PatternId = Guid.NewGuid(), PatternName = "Aaa Other", CollegeId = _otherCollege });
        _context.GradeMasters.AddRange(
            new GradeMaster { GradeMasterId = Guid.NewGuid(), Name = "10 point", CollegeId = _college },
            new GradeMaster { GradeMasterId = Guid.NewGuid(), Name = "7 point", CollegeId = _otherCollege });
        _context.Colleges.AddRange(
            new College { CollegeId = _college, Name = "Mine", CollegeCode = "MINE", CollegeCenter = "C1", ContactEmail = "a@a.edu", ContactPhone = "1", LogoUrl = "logos/x.png" },
            new College { CollegeId = _otherCollege, Name = "Theirs", CollegeCode = "THEIRS", CollegeCenter = "C2", ContactEmail = "b@b.edu", ContactPhone = "2" });
        await _context.SaveChangesAsync();

        var b = await _service.GetBootstrapAsync();

        Assert.Equal(new[] { "2025-2026", "2024-2025", "2023-2024" }, b.AcademicYears.Select(a => a.FullDuration));
        Assert.True(b.AcademicYears[0].IsCurrent);
        Assert.Equal("25-26", b.AcademicYears[0].ShortDuration);
        Assert.Equal(new[] { "Arts", "Pharmacy" }, b.Courses.Select(c => c.Name));
        Assert.Equal(new[] { "ART", "PHM" }, b.Courses.Select(c => c.Code));
        Assert.Equal(new[] { "CBCS", "NEP" }, b.Patterns.Select(p => p.Name));
        Assert.Equal(new[] { "10 point" }, b.GradeMasters.Select(g => g.Name));

        Assert.NotNull(b.College);
        Assert.Equal(_college, b.College!.CollegeId);
        Assert.Equal("Mine", b.College.Name);
        Assert.Equal("MINE", b.College.Code);
        Assert.Equal("C1", b.College.Centre);
        Assert.True(b.College.HasLogo);
        Assert.False(b.College.HasBanner);
    }

    [Fact]
    public async Task Bootstrap_semesters_are_the_one_server_list()
    {
        var b = await _service.GetBootstrapAsync();

        Assert.Equal(10, b.Semesters.Count);
        Assert.Equal(("Sem-1", "Semester I", 1), (b.Semesters[0].Value, b.Semesters[0].Label, b.Semesters[0].Number));
        Assert.Equal(("Sem-6", "Semester VI", 6), (b.Semesters[5].Value, b.Semesters[5].Label, b.Semesters[5].Number));
        Assert.Equal(("Sem-10", "Semester X", 10), (b.Semesters[9].Value, b.Semesters[9].Label, b.Semesters[9].Number));
        Assert.Null(b.College); // no college row seeded
    }

    // ---------------------------------------------------------------- exams

    [Fact]
    public async Task Master_lists_active_and_inactive_but_not_deleted_nor_other_course_ay_or_college()
    {
        Exam("B active");
        Exam("A inactive", active: false);
        Exam("C atkt", type: "ATKT");
        var deleted = Exam("Deleted");
        Exam("Other course", course: _otherCourse);
        Exam("Other AY", ay: _otherAy);
        Exam("Other college", college: _otherCollege);
        await _context.SaveChangesAsync();
        deleted.IsDeleted = true;
        await _context.SaveChangesAsync();

        Assert.Equal(new[] { "A inactive", "B active", "C atkt" }, await Names("master"));
    }

    [Fact]
    public async Task Regular_is_active_regular_type_non_revaluation_only()
    {
        var reg = Exam("Regular ok");
        Exam("Regular inactive", active: false);
        Exam("ATKT", type: "ATKT");
        Exam("Reval of regular", type: "Regular", revalFor: reg.ExamId);
        Exam("Other college", college: _otherCollege);
        await _context.SaveChangesAsync();

        Assert.Equal(new[] { "Regular ok" }, await Names("regular"));
    }

    [Fact]
    public async Task All_is_every_active_type_including_revaluation()
    {
        var reg = Exam("Regular");
        Exam("ATKT", type: "ATKT");
        Exam("Reval", type: "Revaluation", revalFor: reg.ExamId);
        Exam("Inactive", active: false);
        Exam("Other college", college: _otherCollege);
        await _context.SaveChangesAsync();

        Assert.Equal(new[] { "ATKT", "Regular", "Reval" }, await Names("all"));
    }

    [Fact]
    public async Task HallTicket_is_active_non_revaluation_of_any_type()
    {
        var reg = Exam("Regular");
        Exam("ATKT", type: "ATKT");
        Exam("Reval", type: "Revaluation", revalFor: reg.ExamId);
        Exam("Inactive", active: false);
        await _context.SaveChangesAsync();

        Assert.Equal(new[] { "ATKT", "Regular" }, await Names("hallTicket"));
    }

    [Fact]
    public async Task SeatNo_needs_marks_rows_for_the_semester_and_academic_year()
    {
        var withMarks = Exam("With marks");
        var otherSem = Exam("Other semester only");
        var otherAyMarks = Exam("Other AY marks only");
        var inactive = Exam("Inactive with marks", active: false);
        var reval = Exam("Reval with marks", type: "Revaluation", revalFor: withMarks.ExamId);
        Exam("No marks at all");
        var otherCollegeMarks = Exam("Other college marks only");
        Marks(withMarks, "Sem-6");
        Marks(otherSem, "Sem-5");
        Marks(otherAyMarks, "Sem-6", ay: _otherAy);
        Marks(inactive, "Sem-6");
        Marks(reval, "Sem-6");
        Marks(otherCollegeMarks, "Sem-6", college: _otherCollege);
        await _context.SaveChangesAsync();

        Assert.Equal(new[] { "With marks" }, await Names("seatNo", "Sem-6"));
        Assert.Equal(new[] { "Other semester only" }, await Names("seatNo", "Sem-5"));
    }

    [Fact]
    public async Task Process_includes_inactive_ignores_semester_and_stays_in_course_ay_tenant()
    {
        Exam("No semester");
        Exam("Inactive", active: false);
        Exam("Has semester", semester: "Sem-6");
        Exam("Other course", course: _otherCourse);
        Exam("Other college", college: _otherCollege);
        await _context.SaveChangesAsync();

        Assert.Equal(new[] { "Has semester", "Inactive", "No semester" }, await Names("process"));
    }

    [Fact]
    public async Task Exam_rows_carry_display_name_flags_and_are_ordered_by_name()
    {
        var reg = Exam("Zeta", locked: true);
        Exam("Alpha reval", type: "Revaluation", revalFor: reg.ExamId, active: false);
        await _context.SaveChangesAsync();

        var list = (await _service.GetExamsAsync(_course, _ay, "all", null))!;
        Assert.DoesNotContain(list, e => !e.IsActive); // "all" is active only
        var master = (await _service.GetExamsAsync(_course, _ay, "master", null))!;

        Assert.Equal(new[] { "Alpha reval", "Zeta" }, master.Select(e => e.Name));
        var reval = master[0];
        Assert.Equal("Alpha reval (Revaluation)", reval.DisplayName);
        Assert.True(reval.IsRevaluation);
        Assert.Equal(reg.ExamId, reval.RevaluationForExamId);
        Assert.False(reval.IsActive);
        Assert.False(reval.IsLocked);
        Assert.Equal("Revaluation", reval.ExamType);

        var zeta = master[1];
        Assert.Equal("Zeta", zeta.DisplayName);
        Assert.False(zeta.IsRevaluation);
        Assert.Null(zeta.RevaluationForExamId);
        Assert.True(zeta.IsActive);
        Assert.True(zeta.IsLocked);
    }

    [Theory]
    [InlineData("MASTER")]
    [InlineData("seatno")]
    [InlineData(" hallTicket ")]
    public void Purpose_names_are_matched_case_insensitively(string input) =>
        Assert.Contains(ExamPurposes.Normalize(input), ExamPurposes.Names);

    [Theory]
    [InlineData("atkt")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Unknown_purpose_returns_null(string? purpose)
    {
        Assert.Null(ExamPurposes.Normalize(purpose));
        Assert.Null(await _service.GetExamsAsync(_course, _ay, purpose!, null));
    }

    [Fact]
    public void Only_seatNo_requires_a_semester()
    {
        Assert.Equal(new[] { "seatNo" }, ExamPurposes.Names.Where(ExamPurposes.RequiresSemester));
    }

    // ---------------------------------------------------------------- subjects

    [Fact]
    public async Task Subjects_filter_on_course_pattern_semester_ordered_by_code_and_tenant()
    {
        void Add(string code, string name, Guid course, string pattern, string sem, Guid? college = null) =>
            _context.SubjectMasters.Add(new SubjectMaster
            {
                SubjectId = Guid.NewGuid(), SubjectCode = code, Name = name,
                CourseId = course, Pattern = pattern, SemId = sem, CollegeId = college ?? _college,
            });
        Add("PH602", "Second", _course, "NEP", "Sem-6");
        Add("PH601", "First", _course, "NEP", "Sem-6");
        Add("PH501", "Other sem", _course, "NEP", "Sem-5");
        Add("PH603", "Other pattern", _course, "CBCS", "Sem-6");
        Add("XX601", "Other course", _otherCourse, "NEP", "Sem-6");
        Add("ZZ601", "Other college", _course, "NEP", "Sem-6", _otherCollege);
        await _context.SaveChangesAsync();

        var list = await _service.GetSubjectsAsync(_course, "NEP", "Sem-6");

        Assert.Equal(new[] { "PH601", "PH602" }, list.Select(s => s.Code));
        Assert.Equal(new[] { "First", "Second" }, list.Select(s => s.Name));
    }

    // ---------------------------------------------------------------- T-25

    [Fact]
    public async Task ApplyGraceMarks_exam_list_includes_exams_without_a_semester()
    {
        var course = new CourseMaster { CourseId = _course, Name = "Pharmacy", CourseCode = "PHM", CollegeId = _college };
        _context.CourseMasters.Add(course);
        Exam("Created in Exam Master", semester: null);
        Exam("Other AY", ay: _otherAy);
        await _context.SaveChangesAsync();

        var results = new ResultService(_context,
            new EngineRegistry(Array.Empty<IFactProvider>(), Array.Empty<IActionHandler>()));

        var forAy = (await results.GetExamsAsync(_course, "Sem-6", "NEP", _college, _ay)).ToList();
        var anyAy = (await results.GetExamsAsync(_course, "Sem-6", "NEP", _college)).ToList();

        Assert.Equal(new[] { "Created in Exam Master" }, forAy.Select(e => e.ExamName));
        Assert.Equal(2, anyAy.Count);
    }

    // ---------------------------------------------------------------- obsolete endpoints (T-19 D)
    // The old per-screen endpoints are kept for team branches but now delegate to ExamPurposes /
    // LookupService. These pin that each one still answers exactly what its lookup purpose answers.

    private async Task SeedMixedExams()
    {
        var reg = Exam("Regular Nov", "Regular");
        Exam("ATKT Nov", "ATKT");
        Exam("Reval Nov", "Revaluation", revalFor: reg.ExamId);
        Exam("Inactive", active: false);
        Exam("Other AY", ay: _otherAy);
        Exam("Other course", course: _otherCourse);
        Exam("Other college", college: _otherCollege);
        Marks(reg, "Sem-6");
        _context.CourseMasters.Add(new CourseMaster { CourseId = _course, Name = "Pharmacy", CourseCode = "PHM", CollegeId = _college });
        await _context.SaveChangesAsync();
    }

    private async Task<List<Guid>> Ids(string purpose, string? semester = null) =>
        (await _service.GetExamsAsync(_course, _ay, purpose, semester))!.Select(e => e.ExamId).OrderBy(i => i).ToList();

    [Fact]
    public async Task Obsolete_exam_endpoints_return_the_same_exams_as_their_lookup_purpose()
    {
        await SeedMixedExams();
        var repo = new Mock<ExamAPI.Services.Common.IGenericRepository>().Object;
        var req = new ExamAPI.DTOs.GetExam { Courseid = _course, Ayid = _ay };

        var regular = new ExamAPI.Services.RegularExam.RegularExamService(_context, repo);
        Assert.Equal(await Ids("hallTicket"), (await regular.GetExam(req)).Select(e => e.ExamId).OrderBy(i => i));
        Assert.Equal(await Ids("all"), (await regular.GetAllExams(req)).Select(e => e.ExamId).OrderBy(i => i));

        var hall = new ExamAPI.Services.GenerateHallTicket.GenerateHallTicketService(_context);
        var hallRows = await hall.GetExam(req);
        Assert.Equal(await Ids("hallTicket"), hallRows.Select(e => e.ExamId).OrderBy(i => i));
        Assert.Contains(hallRows, r => r.Examname == "ATKT Nov ( ATKT )");

        var seat = new ExamAPI.Services.AssignSeatNo.AssignSeatNoService(_context, repo);
        var seatRows = await seat.GetExam(new ExamAPI.DTOs.GetAssignSeatNoExam { Courseid = _course, Ayid = _ay, Semester = "Sem-6" });
        Assert.Equal(await Ids("seatNo", "Sem-6"), seatRows.Select(e => e.ExamId).OrderBy(i => i));
        Assert.Equal(new[] { "Regular Nov" }, seatRows.Select(e => e.Examname));

        var master = new ExamAPI.Services.Exam.ExamService(_context, repo);
        var masterRows = await master.GetExam(req);
        Assert.Equal(await Ids("master"), masterRows.Select(e => e.ExamId).OrderBy(i => i));
        Assert.Contains(masterRows, r => r.Name == "Reval Nov (Revaluation)" && r.ExamType == "Revaluation");
        Assert.Contains(masterRows, r => r.Name == "Inactive" && r.IsActive == false);
    }

    [Fact]
    public async Task Obsolete_subjects_endpoint_returns_the_same_subjects_as_lookup()
    {
        _context.SubjectMasters.AddRange(
            new SubjectMaster { SubjectId = Guid.NewGuid(), SubjectCode = "PH601", Name = "First", CourseId = _course, Pattern = "NEP", SemId = "Sem-6", CollegeId = _college },
            new SubjectMaster { SubjectId = Guid.NewGuid(), SubjectCode = "PH501", Name = "Other sem", CourseId = _course, Pattern = "NEP", SemId = "Sem-5", CollegeId = _college },
            new SubjectMaster { SubjectId = Guid.NewGuid(), SubjectCode = "ZZ601", Name = "Other college", CourseId = _course, Pattern = "NEP", SemId = "Sem-6", CollegeId = _otherCollege });
        await _context.SaveChangesAsync();

        var old = new ExamAPI.Services.Subject.SubjectService(_context, new Mock<ExamAPI.Services.Common.IGenericRepository>().Object);
        var rows = await old.GetSubjectsAsync(new ExamAPI.DTOs.GetSubjectReqDtos { CourseId = _course, Pattern = "NEP", Semester = "Sem-6" });

        Assert.Equal((await _service.GetSubjectsAsync(_course, "NEP", "Sem-6")).Select(s => s.SubjectId), rows.Select(s => s.SubjectId));
        Assert.Equal(new[] { "First" }, rows.Select(s => s.SubjectName));
    }
    [Fact]
    public async Task Hall_ticket_lists_end_semester_subjects_for_both_ESE_and_ESA_head_names()
    {
        // Engineering names its end-semester head "ESE", pharmacy "ESA"; internal heads stay off the hall ticket.
        Guid Subject(string code, params string[] heads)
        {
            var subject = new SubjectMaster { SubjectId = Guid.NewGuid(), SubjectCode = code, Name = code, CourseId = _course, Pattern = "NEP", SemId = "Sem-6", CollegeId = _college };
            var credit = new SubjectCreditMaster { CreditsId = Guid.NewGuid(), SubjectId = subject.SubjectId, AYID = _ay.ToString(), CollegeId = _college };
            _context.SubjectMasters.Add(subject);
            _context.SubjectCreditMasters.Add(credit);
            foreach (var head in heads)
                _context.SubjectCredits.Add(new SubjectCredits { Id = Guid.NewGuid(), CreditsId = credit.CreditsId, HeadType = head });
            return subject.SubjectId;
        }
        Subject("ENG601", "ESE", "IA");
        Subject("PHM601", "ESA", "TW");
        Subject("TW-ONLY", "TW");
        await _context.SaveChangesAsync();

        var hall = new ExamAPI.Services.GenerateHallTicket.GenerateHallTicketService(_context);
        var rows = await hall.GetHallTicketSubject(new ExamAPI.DTOs.HallTicketSubjectsRequest
            { Ayid = _ay.ToString(), CourseId = _course, Semester = "Sem-6", Pattern = "NEP", ExamId = Guid.NewGuid() });

        Assert.Equal(new[] { "ENG601", "PHM601" }, rows.Select(r => r.SubjectCode).OrderBy(c => c));
    }
}
