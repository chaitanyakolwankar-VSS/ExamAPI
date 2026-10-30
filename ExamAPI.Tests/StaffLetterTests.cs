using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Staff;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// Letter exams and numbers: an exam is all branches' copies together, letter numbers follow the new format and are
/// never reused, the letter settings are checked, and another college's exams and staff are never visible.
/// </summary>
public sealed class StaffLetterTests
{
    private const string Morning = "10:00 AM - 01:00 PM";
    private const string Afternoon = "02:00 PM - 05:00 PM";

    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly StaffLetterService _service;
    private readonly CourseMaster _computer;
    private readonly CourseMaster _it;
    private readonly ExamMaster _computerExam;   // created first: the canonical copy
    private readonly ExamMaster _itExam;
    private readonly StaffMaster _asha;
    private readonly StaffMaster _ravi;

    public StaffLetterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new StaffLetterService(_context);

        _context.Colleges.Add(new College { CollegeId = _college, Name = "VIVA Institute of Technology", CollegeCode = "VIT", CollegeCenter = "Virar", ContactEmail = "office@viva.edu", ContactPhone = "0250" });
        _context.Colleges.Add(new College { CollegeId = _otherCollege, Name = "Other College", CollegeCode = "OTH", CollegeCenter = "Pune", ContactEmail = "o@other.edu", ContactPhone = "020" });
        _context.AcademicYears.Add(new AcademicYear { AYID = _ay, FullDuration = "2026-2027", CollegeId = _college });
        _computer = new CourseMaster { CourseId = Guid.NewGuid(), Name = "Computer", CourseCode = "CMP", CollegeId = _college };
        _it = new CourseMaster { CourseId = Guid.NewGuid(), Name = "Information Technology", CourseCode = "IT", CollegeId = _college };
        _context.CourseMasters.AddRange(_computer, _it);
        _context.SaveChanges();

        // Two branches' copies of one exam; the Computer copy is saved first so it is the canonical one.
        _computerExam = Exam("December 2026", _computer);
        _context.SaveChanges();
        Thread.Sleep(20);
        _itExam = Exam("December 2026", _it);
        _context.SaveChanges();

        // Out of exam order on purpose. 11-11 is Computer only, 12-11 morning is shared by both branches.
        Paper(_computerExam, _computer, "S5-A", "12-11-2026", Morning, "Sem-5");
        Paper(_computerExam, _computer, "S5-B", "13-11-2026", Morning, "Sem-5");
        Paper(_itExam, _it, "S5-C", "12-11-2026", Morning, "Sem-5");
        Paper(_itExam, _it, "S5-D", "11-11-2026", Afternoon, "Sem-5");
        _context.SaveChanges();

        _asha = Person("E101", "Prof.", "Asha", "R.", "Patil", "asha@viva.edu");
        _ravi = Person("E102", "Dr.", "Ravi", null, "Kumar", "ravi@viva.edu");
        _context.SaveChanges();
    }

    private ExamMaster Exam(string name, CourseMaster course, string type = "Regular", Guid? revalFor = null, Guid? college = null)
    {
        var e = new ExamMaster
        {
            ExamId = Guid.NewGuid(), Name = name, ExamType = type, AcademicYearAYID = _ay, CourseId = course.CourseId,
            RevaluationForExamId = revalFor, IsActive = true, CollegeId = college ?? _college
        };
        _context.Exams.Add(e);
        return e;
    }

    private SubjectMaster Paper(ExamMaster exam, CourseMaster course, string code, string date, string time, string sem)
    {
        var s = new SubjectMaster
        {
            SubjectId = Guid.NewGuid(), SubjectCode = code, Name = code + " name", SemId = sem, Pattern = "NEP",
            CourseId = course.CourseId, CollegeId = exam.CollegeId
        };
        _context.SubjectMasters.Add(s);
        _context.TimeTables.Add(new TimeTableMaster
        {
            Id = Guid.NewGuid(), ExamId = exam.ExamId, CourseId = course.CourseId, SubjectId = s.SubjectId, Date = date, Time = time,
            CollegeId = exam.CollegeId
        });
        return s;
    }

    private StaffMaster Person(string code, string title, string first, string? middle, string last, string email,
        bool external = false, bool active = true, Guid? college = null)
    {
        var s = new StaffMaster
        {
            StaffId = Guid.NewGuid(), CollegeId = college ?? _college, EmployeeCode = code, Title = title, FirstName = first,
            MiddleName = middle, LastName = last, Email = email, IsExternal = external, IsActive = active
        };
        _context.StaffMasters.Add(s);
        return s;
    }

    // ------------------------------------------------------------------ exam groups, half, slots

    [Fact]
    public async Task An_exam_is_all_branches_together_and_its_half_comes_from_the_semesters()
    {
        var exams = await _service.ExamsAsync(_ay);

        var exam = Assert.Single(exams);
        Assert.Equal(_computerExam.ExamId, exam.ExamId);          // the earliest created copy stands for the group
        Assert.Equal("December 2026 Regular", exam.Title);
        Assert.Equal("First Half", exam.Half);                    // semester 5 only
        Assert.Equal(2, exam.BranchCount);
        Assert.Equal(3, exam.SlotCount);
        Assert.Equal(0, exam.LetterCount);
    }

    [Fact]
    public async Task Even_semesters_are_the_second_half_and_a_mix_has_no_half()
    {
        var even = Exam("April 2027", _computer);
        var mixed = Exam("June 2027", _computer);
        var noPapers = Exam("Never timetabled", _computer);
        var reval = Exam("December 2026", _computer, revalFor: _computerExam.ExamId);
        _context.SaveChanges();
        Paper(even, _computer, "S6-A", "10-04-2027", Morning, "Sem-6");
        Paper(mixed, _computer, "S1-A", "10-06-2027", Morning, "Sem-1");
        Paper(mixed, _computer, "S2-A", "11-06-2027", Morning, "Sem-2");
        Paper(reval, _computer, "S5-R", "01-02-2027", Morning, "Sem-5");
        _context.SaveChanges();

        var exams = await _service.ExamsAsync(_ay);

        Assert.Equal(new[] { "April 2027", "December 2026", "June 2027" }, exams.Select(e => e.Name));
        Assert.Equal("Second Half", exams[0].Half);
        Assert.Equal("", exams[2].Half);
        Assert.DoesNotContain(exams, e => e.ExamId == noPapers.ExamId);   // no timetable, nothing to supervise
        Assert.Equal(3, exams[1].SlotCount);                              // the revaluation exam is not part of December
    }

    [Fact]
    public async Task Exam_group_helpers_find_every_copy_and_the_canonical_one()
    {
        var copies = await ExamGroups.CopyIdsAsync(_context, _itExam.ExamId);

        Assert.Equal(new[] { _computerExam.ExamId, _itExam.ExamId }.OrderBy(i => i), copies.OrderBy(i => i));
        Assert.Equal(_computerExam.ExamId, await ExamGroups.CanonicalIdAsync(_context, _itExam.ExamId));
        Assert.Equal(_computerExam.ExamId, await ExamGroups.CanonicalIdAsync(_context, _computerExam.ExamId));
        Assert.Null(await ExamGroups.CanonicalIdAsync(_context, Guid.NewGuid()));
    }

    // ------------------------------------------------------------------ letter numbers and settings

    private Task<AppointmentSaveResult> Appoint(SubjectMaster subject, params StaffMaster[] examiners) =>
        _service.SaveAppointmentsAsync(new AppointmentSave
        {
            ExamId = _itExam.ExamId, LetterDate = "2026-10-20",
            Subjects = new List<AppointmentSubjectSave> { new() { SubjectId = subject.SubjectId, Examiners = examiners.Select(e => e.StaffId).ToList() } }
        });

    private SubjectMaster NewPaper()
    {
        var subject = Paper(_computerExam, _computer, "S5-X", "14-11-2026", Morning, "Sem-5");
        _context.SaveChanges();
        return subject;
    }

    [Fact]
    public async Task The_number_is_prefix_year_code_and_a_three_digit_serial()
    {
        var result = await Appoint(NewPaper(), _asha, _ravi);

        Assert.Equal(new[] { "VIT/EXAM/2026-27/EXM-001", "VIT/EXAM/2026-27/EXM-002" }, result.Letters.Select(l => l.LetterNo));
        Assert.Equal(new[] { 1, 2 }, (await _context.StaffLetters.OrderBy(l => l.SerialNo).ToListAsync()).Select(l => l.SerialNo));
    }

    [Fact]
    public async Task The_number_starts_with_the_colleges_own_prefix_when_one_is_set()
    {
        var saved = await _service.SaveSettingsAsync(new LetterSettingsSave { LetterPrefix = "  VIVA/EXAM-CELL ", CapContact = "98200 11111, 98200 22222" });
        Assert.Equal("VIVA/EXAM-CELL", saved.LetterPrefix);
        Assert.Equal("98200 11111, 98200 22222", saved.CapContact);
        Assert.Equal("VIT/EXAM", saved.DefaultPrefix);

        var subject = NewPaper();
        var result = await Appoint(subject, _asha);
        Assert.Equal("VIVA/EXAM-CELL/2026-27/EXM-001", result.Letters.Single().LetterNo);

        // Cleared again: back to the college code.
        await _service.SaveSettingsAsync(new LetterSettingsSave { LetterPrefix = "", CapContact = " " });
        var settings = await _service.GetSettingsAsync();
        Assert.Null(settings.LetterPrefix);
        Assert.Null(settings.CapContact);
        var next = await Appoint(subject, _asha, _ravi);
        Assert.Equal("VIT/EXAM/2026-27/EXM-002", next.Letters.Single(l => l.StaffId == _ravi.StaffId).LetterNo);
    }

    [Fact]
    public async Task The_academic_years_short_name_is_used_when_it_has_one()
    {
        var year = await _context.AcademicYears.SingleAsync();
        year.ShortDuration = "26-27";
        await _context.SaveChangesAsync();

        var result = await Appoint(NewPaper(), _asha);

        Assert.Equal("VIT/EXAM/26-27/EXM-001", result.Letters.Single().LetterNo);
        Assert.Equal("2026-27", StaffLetterService.ShortYear("2026-2027"));
        Assert.Equal("2026-27", StaffLetterService.ShortYear("2026-27"));
        Assert.Equal("FY 2026", StaffLetterService.ShortYear("FY 2026"));
    }

    [Fact]
    public async Task Letter_settings_refuse_a_bad_prefix()
    {
        var bad = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSettingsAsync(new LetterSettingsSave { LetterPrefix = "VIVA EXAM" }));
        Assert.Contains("letters, digits", bad.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSettingsAsync(new LetterSettingsSave { LetterPrefix = new string('A', 41) }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSettingsAsync(new LetterSettingsSave { CapContact = new string('9', 101) }));
        Assert.Empty(await _context.CollegeSettings.ToListAsync());
    }

    [Fact]
    public async Task The_reporting_place_and_guidelines_are_saved_cleaned_and_fall_back_to_the_defaults()
    {
        var empty = await _service.GetSettingsAsync();
        Assert.Null(empty.ReportingPlace);
        Assert.Null(empty.DutyGuidelines);
        Assert.Equal("the Examination Control Room", empty.DefaultReportingPlace);
        Assert.Equal(3, empty.DefaultGuidelines.Count);
        Assert.Equal("Follow the University's examination conduct rules and regulations.", empty.DefaultGuidelines[0]);

        var saved = await _service.SaveSettingsAsync(new LetterSettingsSave
        {
            ReportingPlace = "  Room 12, Block A ", DutyGuidelines = " Be on time. \r\n\r\n  Carry your ID card.  \n"
        });
        Assert.Equal("Room 12, Block A", saved.ReportingPlace);
        Assert.Equal("Be on time.\nCarry your ID card.", saved.DutyGuidelines);
        Assert.Equal(new[] { "Be on time.", "Carry your ID card." }, DutyLetterDefaults.GuidelinesOf(saved.DutyGuidelines));

        var cleared = await _service.SaveSettingsAsync(new LetterSettingsSave { ReportingPlace = " ", DutyGuidelines = "\n " });
        Assert.Null(cleared.ReportingPlace);
        Assert.Null(cleared.DutyGuidelines);
        Assert.Equal(DutyLetterDefaults.Guidelines, DutyLetterDefaults.GuidelinesOf(cleared.DutyGuidelines));
    }

    [Fact]
    public async Task The_reporting_place_and_guidelines_are_limited()
    {
        var place = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSettingsAsync(new LetterSettingsSave { ReportingPlace = new string('R', 201) }));
        Assert.Contains("200", place.Message);
        var many = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSettingsAsync(new LetterSettingsSave { DutyGuidelines = string.Join("\n", Enumerable.Range(1, 11).Select(i => "Rule " + i)) }));
        Assert.Contains("more than 10", many.Message);
        var longLine = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSettingsAsync(new LetterSettingsSave { DutyGuidelines = new string('G', 301) }));
        Assert.Contains("300", longLine.Message);
        var total = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSettingsAsync(new LetterSettingsSave { DutyGuidelines = string.Join("\n", Enumerable.Range(1, 8).Select(_ => new string('G', 290))) }));
        Assert.Contains("2000", total.Message);
        Assert.Empty(await _context.CollegeSettings.ToListAsync());
    }

    [Fact]
    public async Task The_email_signature_is_saved_cleaned_and_limited()
    {
        Assert.Null((await _service.GetSettingsAsync()).LetterEmailSignature);

        var saved = await _service.SaveSettingsAsync(new LetterSettingsSave { LetterEmailSignature = " \r\n  Thanks & Regards,   \r\n\r\nRegistrar  \nVIVA College \n\n " });
        Assert.Equal("  Thanks & Regards,\n\nRegistrar\nVIVA College", saved.LetterEmailSignature);   // line ends tidied, empty lines at both ends gone, the one inside kept
        Assert.Equal("  Thanks & Regards,\n\nRegistrar\nVIVA College", (await _service.GetSettingsAsync()).LetterEmailSignature);
        Assert.Contains("email signature: set", (await _context.AuditLogs.Where(a => a.Event == "LetterSettingsChanged").OrderByDescending(a => a.Timestamp).FirstAsync()).Details);

        // Empty text clears it.
        var cleared = await _service.SaveSettingsAsync(new LetterSettingsSave { LetterEmailSignature = " \n  " });
        Assert.Null(cleared.LetterEmailSignature);

        var exactly = await _service.SaveSettingsAsync(new LetterSettingsSave { LetterEmailSignature = new string('S', 1000) });
        Assert.Equal(1000, exactly.LetterEmailSignature!.Length);
        var tooLong = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSettingsAsync(new LetterSettingsSave { LetterEmailSignature = new string('S', 1001) }));
        Assert.Contains("1000", tooLong.Message);
        Assert.Equal(1000, (await _service.GetSettingsAsync()).LetterEmailSignature!.Length);   // the refused save changed nothing
    }

    // ------------------------------------------------------------------ refusals and tenancy

    [Fact]
    public async Task Revaluation_exams_are_not_for_letters()
    {
        var reval = Exam("December 2026", _computer, revalFor: _computerExam.ExamId);
        _context.SaveChanges();
        Paper(reval, _computer, "S5-R", "01-02-2027", Morning, "Sem-5");
        _context.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AppointmentsAsync(reval.ExamId));
    }

    [Fact]
    public async Task Another_colleges_exams_and_staff_are_invisible()
    {
        var theirCourse = new CourseMaster { CourseId = Guid.NewGuid(), Name = "Mechanical", CourseCode = "MEC", CollegeId = _otherCollege };
        _context.CourseMasters.Add(theirCourse);
        var theirExam = Exam("December 2026", theirCourse, college: _otherCollege);
        _context.SaveChanges();
        Paper(theirExam, theirCourse, "M5-A", "12-11-2026", Morning, "Sem-5");
        var theirStaff = Person("E900", "Mr.", "Their", null, "Person", "their@other.edu", college: _otherCollege);
        _context.StaffLetters.Add(new StaffLetter
        {
            Id = Guid.NewGuid(), CollegeId = _otherCollege, StaffId = theirStaff.StaffId, LetterType = StaffLetterTypes.Examiner,
            ExamId = theirExam.ExamId, AYID = _ay, SerialNo = 7, LetterNo = "OTH/EXAM/2026-27/EXM-007", LetterDate = new DateTime(2026, 10, 1),
            Role = "Examiner", StaffName = "Mr. Their Person", ExamTitle = "December 2026 Regular"
        });
        _context.SaveChanges();

        // Their exam is not in our list, and asking for it by ID finds nothing.
        var exams = await _service.ExamsAsync(_ay);
        Assert.Equal(_computerExam.ExamId, Assert.Single(exams).ExamId);
        Assert.Equal(3, exams[0].SlotCount);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.AppointmentsAsync(theirExam.ExamId));

        // Their staff cannot be given our letters, and their letters do not show up here or move our numbers.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Appoint(NewPaper(), theirStaff));
        Assert.Contains("not found", ex.Message);
        var ours = await Appoint(NewPaper(), _asha);
        Assert.Equal("VIT/EXAM/2026-27/EXM-001", ours.Letters.Single().LetterNo);
        Assert.Single((await _service.AppointmentsAsync(_computerExam.ExamId)).Letters);
    }
}
