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
/// Supervision letters: an exam is all branches' copies together, the grid creates numbered letters that keep their
/// number, a bad grid saves nothing, and another college's exams and staff are never visible.
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

    private static DutyDto Duty(string date, string time) => new() { Date = date, Time = time };

    private static SupervisionAssignment Assign(StaffMaster staff, string role, params DutyDto[] duties) =>
        new() { StaffId = staff.StaffId, Role = role, Duties = duties.ToList() };

    private static SupervisionSave Grid(Guid examId, string date, params SupervisionAssignment[] assignments) =>
        new() { ExamId = examId, LetterDate = date, Assignments = assignments.ToList() };

    private Task<SupervisionSaveResult> Save(string date, params SupervisionAssignment[] assignments) =>
        _service.SaveSupervisionAsync(Grid(_itExam.ExamId, date, assignments));   // any copy will do

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
    public async Task The_slots_are_the_distinct_dates_and_times_of_all_branches_in_exam_order()
    {
        var itCopy = await _service.SupervisionAsync(_itExam.ExamId);
        var computerCopy = await _service.SupervisionAsync(_computerExam.ExamId);

        Assert.Equal(_computerExam.ExamId, itCopy.Exam.ExamId);          // whichever copy is asked for
        Assert.Equal(computerCopy.Slots.Select(s => (s.Date, s.Time)), itCopy.Slots.Select(s => (s.Date, s.Time)));
        Assert.Equal(new[] { ("11-11-2026", Afternoon), ("12-11-2026", Morning), ("13-11-2026", Morning) },
            itCopy.Slots.Select(s => (s.Date, s.Time)));
        var shared = itCopy.Slots[1];
        Assert.Equal(2, shared.Papers);
        Assert.Equal(new[] { "Computer", "Information Technology" }, shared.Branches);
        Assert.Equal(new[] { "Information Technology" }, itCopy.Slots[0].Branches);
        Assert.Equal(SupervisionRoles.All, itCopy.Roles);
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

    // ------------------------------------------------------------------ saving

    [Fact]
    public async Task Saving_the_grid_creates_numbered_letters_against_the_canonical_exam()
    {
        var result = await Save("2026-10-20",
            Assign(_asha, SupervisionRoles.Senior, Duty("13-11-2026", Morning), Duty("12-11-2026", Morning)),
            Assign(_ravi, SupervisionRoles.Junior, Duty("11-11-2026", Afternoon)));

        Assert.Equal((2, 0, 0, 0), (result.Created, result.Updated, result.Unchanged, result.Removed));
        Assert.Equal("Saved: 2 new, 0 updated, 0 unchanged, 0 cancelled.", result.Message);
        Assert.Equal(new[] { "VIT/EXAM/SUP/2026-27/0001", "VIT/EXAM/SUP/2026-27/0002" }, result.Letters.Select(l => l.LetterNo));
        var asha = result.Letters[0];
        Assert.Equal("Prof. Asha R. Patil", asha.StaffName);
        Assert.Equal("2026-10-20", asha.LetterDate);
        Assert.Equal(SupervisionRoles.Senior, asha.Role);
        Assert.Equal(new[] { "12-11-2026", "13-11-2026" }, asha.Duties.Select(d => d.Date));   // exam order, not the order sent

        var stored = await _context.StaffLetters.ToListAsync();
        Assert.All(stored, l => Assert.Equal(_computerExam.ExamId, l.ExamId));
        Assert.All(stored, l => Assert.Equal(StaffLetterTypes.Supervision, l.LetterType));
        Assert.All(stored, l => Assert.Equal("December 2026 Regular", l.ExamTitle));
        Assert.All(stored, l => Assert.Equal("First Half", l.Half));
        Assert.All(stored, l => Assert.Equal(_college, l.CollegeId));
        Assert.Equal(3, await _context.StaffLetterDuties.CountAsync());

        var screen = await _service.SupervisionAsync(_computerExam.ExamId);
        Assert.Equal(2, screen.Exam.LetterCount);
        Assert.Equal(2, screen.Letters.Count);
    }

    [Fact]
    public async Task Saving_again_updates_role_and_duties_but_keeps_the_number_and_date()
    {
        await Save("2026-10-20",
            Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning)),
            Assign(_ravi, SupervisionRoles.Junior, Duty("13-11-2026", Morning)));

        var result = await Save("2026-11-01",
            Assign(_asha, SupervisionRoles.ChiefConductor, Duty("12-11-2026", Morning), Duty("13-11-2026", Morning)),
            Assign(_ravi, SupervisionRoles.Junior, Duty("13-11-2026", Morning)));

        Assert.Equal((0, 1, 1, 0), (result.Created, result.Updated, result.Unchanged, result.Removed));
        var asha = result.Letters.Single(l => l.StaffId == _asha.StaffId);
        Assert.Equal("VIT/EXAM/SUP/2026-27/0001", asha.LetterNo);
        Assert.Equal("2026-10-20", asha.LetterDate);                    // the new date is for new letters only
        Assert.Equal(SupervisionRoles.ChiefConductor, asha.Role);
        Assert.Equal(2, asha.Duties.Count);
        Assert.Equal(3, await _context.StaffLetterDuties.CountAsync());  // the old duties are replaced, not piled up
        Assert.Equal(2, await _context.StaffLetters.CountAsync());
    }

    [Fact]
    public async Task A_person_left_out_of_the_grid_loses_the_letter_and_the_number_is_never_reused()
    {
        await Save("2026-10-20",
            Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning)),
            Assign(_ravi, SupervisionRoles.Junior, Duty("13-11-2026", Morning)));
        var meena = Person("E103", "Dr.", "Meena", null, "Shah", "meena@viva.edu");
        _context.SaveChanges();

        // Ravi (0002) is cancelled, Meena is new: she gets 0003, not 0002.
        var result = await Save("2026-10-25",
            Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning)),
            Assign(meena, SupervisionRoles.Peon, Duty("13-11-2026", Morning)));

        Assert.Equal((1, 0, 1, 1), (result.Created, result.Updated, result.Unchanged, result.Removed));
        Assert.Equal(new[] { "VIT/EXAM/SUP/2026-27/0001", "VIT/EXAM/SUP/2026-27/0003" }, result.Letters.Select(l => l.LetterNo));
        var cancelled = await _context.StaffLetters.IgnoreQueryFilters().SingleAsync(l => l.StaffId == _ravi.StaffId);
        Assert.True(cancelled.IsDeleted);
        Assert.Equal("VIT/EXAM/SUP/2026-27/0002", cancelled.LetterNo);

        // Ravi can be given duty again: a fresh letter with the next number.
        var again = await Save("2026-10-26",
            Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning)),
            Assign(meena, SupervisionRoles.Peon, Duty("13-11-2026", Morning)),
            Assign(_ravi, SupervisionRoles.Junior, Duty("13-11-2026", Morning)));
        Assert.Equal("VIT/EXAM/SUP/2026-27/0004", again.Letters.Single(l => l.StaffId == _ravi.StaffId).LetterNo);
    }

    [Fact]
    public async Task An_empty_grid_cancels_every_letter()
    {
        await Save("2026-10-20", Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning)));

        var result = await Save("2026-10-20");

        Assert.Equal(1, result.Removed);
        Assert.Empty(result.Letters);
        Assert.Empty(await _context.StaffLetters.ToListAsync());
    }

    [Fact]
    public async Task The_number_starts_with_the_colleges_own_prefix_when_one_is_set_and_uses_the_short_year()
    {
        var saved = await _service.SaveSettingsAsync(new LetterSettingsSave { LetterPrefix = "  VIVA/EXAM-CELL ", CapContact = "98200 11111, 98200 22222" });
        Assert.Equal("VIVA/EXAM-CELL", saved.LetterPrefix);
        Assert.Equal("98200 11111, 98200 22222", saved.CapContact);
        Assert.Equal("VIT/EXAM", saved.DefaultPrefix);

        var result = await Save("2026-10-20", Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning)));
        Assert.Equal("VIVA/EXAM-CELL/SUP/2026-27/0001", result.Letters.Single().LetterNo);

        // Cleared again: back to the college code.
        await _service.SaveSettingsAsync(new LetterSettingsSave { LetterPrefix = "", CapContact = " " });
        var settings = await _service.GetSettingsAsync();
        Assert.Null(settings.LetterPrefix);
        Assert.Null(settings.CapContact);
        var next = await Save("2026-10-20",
            Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning)),
            Assign(_ravi, SupervisionRoles.Junior, Duty("12-11-2026", Morning)));
        Assert.Equal("VIT/EXAM/SUP/2026-27/0002", next.Letters.Single(l => l.StaffId == _ravi.StaffId).LetterNo);
    }

    [Fact]
    public async Task The_academic_years_short_name_is_used_when_it_has_one()
    {
        var year = await _context.AcademicYears.SingleAsync();
        year.ShortDuration = "26-27";
        await _context.SaveChangesAsync();

        var result = await Save("2026-10-20", Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning)));

        Assert.Equal("VIT/EXAM/SUP/26-27/0001", result.Letters.Single().LetterNo);
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

    // ------------------------------------------------------------------ refusals

    [Fact]
    public async Task A_bad_grid_is_refused_with_every_problem_listed_and_nothing_is_saved()
    {
        var external = Person("", "Dr.", "Neha", null, "Iyer", "neha@other.edu", external: true);
        var left = Person("E104", "Mr.", "Om", null, "Das", "om@viva.edu", active: false);
        _context.SaveChanges();
        var ok = Duty("12-11-2026", Morning);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-20",
            Assign(_asha, "Head Boy", ok),
            Assign(_ravi, SupervisionRoles.Junior, Duty("14-11-2026", Morning)),
            Assign(external, SupervisionRoles.Junior, ok),
            Assign(left, SupervisionRoles.Junior, ok),
            Assign(_asha, SupervisionRoles.Junior, ok),
            Assign(new StaffMaster { StaffId = Guid.NewGuid(), Title = "Mr.", FirstName = "Nobody", Email = "x@y.z" }, SupervisionRoles.Peon, ok)));

        Assert.Contains("Prof. Asha R. Patil needs a role", ex.Message);
        Assert.Contains("Dr. Ravi Kumar has a duty on 14-11-2026", ex.Message);
        Assert.Contains("Dr. Neha Iyer is an external member of staff", ex.Message);
        Assert.Contains("Mr. Om Das has left", ex.Message);
        Assert.Contains("Prof. Asha R. Patil appears more than once", ex.Message);
        Assert.Contains("a member of staff in the list was not found", ex.Message);
        Assert.Empty(await _context.StaffLetters.ToListAsync());
        Assert.Empty(await _context.StaffLetterDuties.ToListAsync());
    }

    [Fact]
    public async Task A_person_needs_a_role_and_at_least_one_date_and_the_date_must_be_a_real_slot()
    {
        var noDate = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-20", Assign(_asha, SupervisionRoles.Senior)));
        Assert.Equal("Prof. Asha R. Patil needs at least one date.", noDate.Message);

        var wrongTime = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-20", Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Afternoon))));
        Assert.Contains("which is not a date and time of this exam", wrongTime.Message);

        var noRole = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-20", Assign(_asha, " ", Duty("12-11-2026", Morning))));
        Assert.Contains("needs a role", noRole.Message);

        var badDate = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("20/10/2026", Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning))));
        Assert.Contains("letter date is not a valid date", badDate.Message);

        var missingDate = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("", Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning))));
        Assert.Equal("Enter the letter date.", missingDate.Message);

        Assert.Empty(await _context.StaffLetters.ToListAsync());
    }

    [Fact]
    public async Task A_bad_grid_leaves_the_existing_letters_as_they_were()
    {
        await Save("2026-10-20", Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-20",
            Assign(_ravi, SupervisionRoles.Junior, Duty("12-11-2026", Morning)),
            Assign(_asha, "Nope", Duty("12-11-2026", Morning))));

        var letter = await _context.StaffLetters.SingleAsync();
        Assert.Equal(_asha.StaffId, letter.StaffId);
        Assert.Equal(SupervisionRoles.Senior, letter.Role);
        Assert.False(letter.IsDeleted);
    }

    [Fact]
    public async Task Revaluation_exams_are_not_for_supervision()
    {
        var reval = Exam("December 2026", _computer, revalFor: _computerExam.ExamId);
        _context.SaveChanges();
        Paper(reval, _computer, "S5-R", "01-02-2027", Morning, "Sem-5");
        _context.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SupervisionAsync(reval.ExamId));
    }

    // ------------------------------------------------------------------ tenancy

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
            Id = Guid.NewGuid(), CollegeId = _otherCollege, StaffId = theirStaff.StaffId, LetterType = StaffLetterTypes.Supervision,
            ExamId = theirExam.ExamId, AYID = _ay, SerialNo = 7, LetterNo = "OTH/EXAM/SUP/2026-27/0007", LetterDate = new DateTime(2026, 10, 1),
            Role = SupervisionRoles.Senior, StaffName = "Mr. Their Person", ExamTitle = "December 2026 Regular"
        });
        _context.SaveChanges();

        // Their exam is not in our list, and asking for it by ID finds nothing.
        var exams = await _service.ExamsAsync(_ay);
        Assert.Equal(_computerExam.ExamId, Assert.Single(exams).ExamId);
        Assert.Equal(3, exams[0].SlotCount);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SupervisionAsync(theirExam.ExamId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SaveSupervisionAsync(Grid(theirExam.ExamId, "2026-10-20")));

        // Their staff cannot be given our duties, and their letters do not show up here or move our numbers.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-20", Assign(theirStaff, SupervisionRoles.Senior, Duty("12-11-2026", Morning))));
        Assert.Contains("not found", ex.Message);
        var ours = await Save("2026-10-20", Assign(_asha, SupervisionRoles.Senior, Duty("12-11-2026", Morning)));
        Assert.Equal("VIT/EXAM/SUP/2026-27/0001", ours.Letters.Single().LetterNo);
        Assert.Single((await _service.SupervisionAsync(_computerExam.ExamId)).Letters);
    }
}
