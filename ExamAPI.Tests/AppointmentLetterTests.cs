using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Staff;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// Examiner and moderator letters: one screen per exam (all branches together), a letter per person per subject per
/// type with its own number series, replace-the-whole-screen saving, and a bad screen saves nothing.
/// </summary>
public sealed class AppointmentLetterTests
{
    private const string Morning = "10:00 AM - 01:00 PM";

    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly StaffLetterService _service;
    private readonly CourseMaster _computer;
    private readonly CourseMaster _it;
    private readonly ExamMaster _computerExam;   // created first: the canonical copy
    private readonly ExamMaster _itExam;
    private readonly SubjectMaster _s6Late;      // Computer, semester 6, listed second by SortOrder
    private readonly SubjectMaster _s6First;     // Computer, semester 6, listed first by SortOrder
    private readonly SubjectMaster _s5;          // Computer, semester 5
    private readonly SubjectMaster _itSubject;   // IT, semester 5, timetabled for both copies
    private readonly StaffMaster _asha;
    private readonly StaffMaster _ravi;
    private readonly StaffMaster _meena;

    public AppointmentLetterTests()
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

        _computerExam = Exam("December 2026", _computer);
        _context.SaveChanges();
        Thread.Sleep(20);
        _itExam = Exam("December 2026", _it);
        _context.SaveChanges();

        // Added out of the order they should be listed in.
        _s6Late = Subject("S6-Z", "Compilers", "Sem-6", _computer, sortOrder: 2);
        _s5 = Subject("S5-A", "Databases", "Sem-5", _computer, sortOrder: 1);
        _s6First = Subject("S6-B", "Networks", "Sem-6", _computer, sortOrder: 1);
        _itSubject = Subject("IT5-A", "Web Design", "Sem-5", _it, sortOrder: null);
        Paper(_computerExam, _computer, _s6Late, "12-11-2026");
        Paper(_computerExam, _computer, _s5, "13-11-2026");
        Paper(_computerExam, _computer, _s6First, "14-11-2026");
        Paper(_itExam, _it, _itSubject, "12-11-2026");
        Paper(_computerExam, _computer, _itSubject, "15-11-2026");   // the same subject on another copy's timetable too
        _context.SaveChanges();

        _asha = Person("E101", "Prof.", "Asha", "R.", "Patil", "asha@viva.edu");
        _ravi = Person("E102", "Dr.", "Ravi", null, "Kumar", "ravi@viva.edu");
        _meena = Person("E103", "Dr.", "Meena", null, "Shah", "meena@viva.edu");
        _context.SaveChanges();
    }

    private ExamMaster Exam(string name, CourseMaster course, Guid? revalFor = null)
    {
        var e = new ExamMaster
        {
            ExamId = Guid.NewGuid(), Name = name, ExamType = "Regular", AcademicYearAYID = _ay, CourseId = course.CourseId,
            RevaluationForExamId = revalFor, IsActive = true, CollegeId = _college
        };
        _context.Exams.Add(e);
        return e;
    }

    private SubjectMaster Subject(string code, string name, string sem, CourseMaster course, int? sortOrder)
    {
        var s = new SubjectMaster
        {
            SubjectId = Guid.NewGuid(), SubjectCode = code, Name = name, SemId = sem, Pattern = "NEP", SortOrder = sortOrder,
            CourseId = course.CourseId, CollegeId = _college
        };
        _context.SubjectMasters.Add(s);
        return s;
    }

    private void Paper(ExamMaster exam, CourseMaster course, SubjectMaster subject, string date)
    {
        _context.TimeTables.Add(new TimeTableMaster
        {
            Id = Guid.NewGuid(), ExamId = exam.ExamId, CourseId = course.CourseId, SubjectId = subject.SubjectId, Date = date, Time = Morning,
            CollegeId = exam.CollegeId
        });
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

    private static AppointmentSubjectSave Pick(SubjectMaster subject, StaffMaster[]? examiners = null, StaffMaster[]? moderators = null) => new()
    {
        SubjectId = subject.SubjectId,
        Examiners = (examiners ?? Array.Empty<StaffMaster>()).Select(s => s.StaffId).ToList(),
        Moderators = (moderators ?? Array.Empty<StaffMaster>()).Select(s => s.StaffId).ToList()
    };

    private Task<AppointmentSaveResult> Save(string date, params AppointmentSubjectSave[] subjects) =>
        _service.SaveAppointmentsAsync(new AppointmentSave { ExamId = _itExam.ExamId, LetterDate = date, Subjects = subjects.ToList() });   // any copy will do

    // ------------------------------------------------------------------ the screen

    [Fact]
    public async Task The_subjects_of_all_branches_are_listed_once_by_branch_semester_and_subject_order()
    {
        var screen = await _service.AppointmentsAsync(_itExam.ExamId);

        Assert.Equal(_computerExam.ExamId, screen.Exam.ExamId);
        Assert.Equal(new[] { "S5-A", "S6-B", "S6-Z", "IT5-A" }, screen.Subjects.Select(s => s.Code));   // Computer 5, 6 (by SortOrder), then IT
        var first = screen.Subjects[0];
        Assert.Equal(("Databases", "Computer", 5, "NEP"), (first.Name, first.Branch, first.Semester, first.Pattern));
        Assert.Equal(_computer.CourseId, first.CourseId);
        Assert.Equal(_s5.SubjectId, first.SubjectId);
        Assert.Equal("Information Technology", screen.Subjects[3].Branch);
        Assert.Empty(screen.Letters);
        Assert.Equal(0, screen.Exam.AppointmentCount);
    }

    [Fact]
    public async Task Exams_count_their_appointment_letters_and_no_supervision_letters()
    {
        await Save("2026-10-20", Pick(_s5, new[] { _asha }, new[] { _ravi }));
        // A duty chart's letter belongs to no exam and is not counted.
        _context.StaffLetters.Add(new StaffLetter
        {
            Id = Guid.NewGuid(), CollegeId = _college, StaffId = _meena.StaffId, LetterType = StaffLetterTypes.Supervision,
            DutyChartId = Guid.NewGuid(), AYID = _ay, SerialNo = 1, LetterNo = "VIT/EXAM/2026-27/DUTY-001", LetterDate = new DateTime(2026, 10, 1),
            Role = SupervisionRoles.Senior, StaffName = "Dr. Meena Shah", ExamTitle = "May 2026"
        });
        _context.SaveChanges();

        var exam = Assert.Single(await _service.ExamsAsync(_ay));
        Assert.Equal(0, exam.LetterCount);
        Assert.Equal(2, exam.AppointmentCount);
        var screen = await _service.AppointmentsAsync(_computerExam.ExamId);
        Assert.Equal((0, 2), (screen.Exam.LetterCount, screen.Exam.AppointmentCount));
        Assert.Equal(2, screen.Letters.Count);
    }

    [Fact]
    public async Task Revaluation_exams_have_no_appointment_letters()
    {
        var reval = Exam("December 2026", _computer, revalFor: _computerExam.ExamId);
        _context.SaveChanges();
        Paper(reval, _computer, _s5, "01-02-2027");
        _context.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AppointmentsAsync(reval.ExamId));
        Assert.Contains("Appointment letters are for regular exams", ex.Message);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.AppointmentsAsync(Guid.NewGuid()));
    }

    // ------------------------------------------------------------------ saving and numbering

    [Fact]
    public async Task Saving_creates_numbered_letters_with_a_separate_series_for_examiners_and_moderators()
    {
        var result = await Save("2026-10-20",
            Pick(_s5, new[] { _asha }, new[] { _ravi }),
            Pick(_s6First, new[] { _ravi, _meena }, new[] { _asha }));

        Assert.Equal((5, 0, 0), (result.Created, result.Unchanged, result.Removed));
        Assert.Equal("Saved: 5 new, 0 unchanged, 0 cancelled.", result.Message);
        var examiners = result.Letters.Where(l => l.Type == "EXAMINER").ToList();
        var moderators = result.Letters.Where(l => l.Type == "MODERATOR").ToList();
        Assert.Equal(new[] { "VIT/EXAM/2026-27/EXM-001", "VIT/EXAM/2026-27/EXM-002", "VIT/EXAM/2026-27/EXM-003" }, examiners.Select(l => l.LetterNo));
        Assert.Equal(new[] { "VIT/EXAM/2026-27/MOD-001", "VIT/EXAM/2026-27/MOD-002" }, moderators.Select(l => l.LetterNo));
        Assert.Equal(new[] { "EXAMINER", "EXAMINER", "EXAMINER", "MODERATOR", "MODERATOR" }, result.Letters.Select(l => l.Type));
        Assert.All(result.Letters, l => Assert.Equal("2026-10-20", l.LetterDate));
        Assert.Equal(_s5.SubjectId, examiners[0].SubjectId);
        Assert.Equal(_asha.StaffId, examiners[0].StaffId);
        Assert.Equal("Prof. Asha R. Patil", examiners[0].StaffName);

        var stored = await _context.StaffLetters.ToListAsync();
        Assert.All(stored, l => Assert.Equal(_computerExam.ExamId, l.ExamId));          // against the canonical copy
        Assert.All(stored, l => Assert.Equal("December 2026 Regular", l.ExamTitle));
        Assert.All(stored, l => Assert.Equal("", l.Half));   // semesters 5 and 6 together: no half
        Assert.All(stored, l => Assert.Equal(_college, l.CollegeId));
        Assert.All(stored, l => Assert.Equal(_ay, l.AYID));
        Assert.Equal(3, stored.Count(l => l.Role == "Examiner"));
        Assert.Equal(2, stored.Count(l => l.Role == "Moderator"));
        Assert.Empty(await _context.StaffLetterDuties.ToListAsync());

        var screen = await _service.AppointmentsAsync(_computerExam.ExamId);
        Assert.Equal(5, screen.Exam.AppointmentCount);
        Assert.Equal(5, screen.Letters.Count);
    }

    [Fact]
    public async Task Saving_the_same_screen_again_keeps_every_number_and_date()
    {
        await Save("2026-10-20", Pick(_s5, new[] { _asha }, new[] { _ravi }));

        var result = await Save("2026-11-05", Pick(_s5, new[] { _asha }, new[] { _ravi }));

        Assert.Equal((0, 2, 0), (result.Created, result.Unchanged, result.Removed));
        Assert.Equal("Saved: 0 new, 2 unchanged, 0 cancelled.", result.Message);
        Assert.All(result.Letters, l => Assert.Equal("2026-10-20", l.LetterDate));
        Assert.Equal("VIT/EXAM/2026-27/EXM-001", result.Letters.Single(l => l.Type == "EXAMINER").LetterNo);
        Assert.Equal(2, await _context.StaffLetters.CountAsync());

        // No new letters: the date may be left out.
        var noDate = await Save("", Pick(_s5, new[] { _asha }, new[] { _ravi }));
        Assert.Equal(2, noDate.Unchanged);
    }

    [Fact]
    public async Task Removing_an_appointment_cancels_the_letter_and_its_number_is_never_reused()
    {
        await Save("2026-10-20", Pick(_s5, new[] { _asha, _ravi }, new[] { _meena }));   // EXM 0001, 0002; MOD 0001

        // Ravi (EXM 0002) and Meena (MOD 0001) are dropped; Meena becomes an examiner of another subject: a fresh EXM 0003.
        var result = await Save("2026-10-25", Pick(_s5, new[] { _asha }), Pick(_s6First, new[] { _meena }));

        Assert.Equal((1, 1, 2), (result.Created, result.Unchanged, result.Removed));
        Assert.Equal("Saved: 1 new, 1 unchanged, 2 cancelled.", result.Message);
        Assert.Equal(new[] { "VIT/EXAM/2026-27/EXM-001", "VIT/EXAM/2026-27/EXM-003" }, result.Letters.Select(l => l.LetterNo));
        var cancelled = await _context.StaffLetters.IgnoreQueryFilters().Where(l => l.IsDeleted).ToListAsync();
        Assert.Equal(new[] { "VIT/EXAM/2026-27/EXM-002", "VIT/EXAM/2026-27/MOD-001" }, cancelled.Select(l => l.LetterNo).OrderBy(n => n));

        // Moderator numbers continue after the cancelled 0001, and Ravi can be appointed again with a new number.
        var again = await Save("2026-10-26", Pick(_s5, new[] { _asha, _ravi }, new[] { _meena }), Pick(_s6First, new[] { _meena }));
        Assert.Equal("VIT/EXAM/2026-27/MOD-002", again.Letters.Single(l => l.Type == "MODERATOR").LetterNo);
        Assert.Equal("VIT/EXAM/2026-27/EXM-004", again.Letters.Single(l => l.StaffId == _ravi.StaffId).LetterNo);
    }

    [Fact]
    public async Task An_empty_screen_cancels_every_appointment()
    {
        await Save("2026-10-20", Pick(_s5, new[] { _asha }, new[] { _ravi }));

        var result = await Save("2026-10-20");

        Assert.Equal(2, result.Removed);
        Assert.Empty(result.Letters);
        Assert.Empty(await _context.StaffLetters.ToListAsync());
    }

    [Fact]
    public async Task A_person_may_be_an_examiner_of_one_subject_and_a_moderator_of_another()
    {
        var result = await Save("2026-10-20", Pick(_s5, new[] { _asha }), Pick(_s6First, moderators: new[] { _asha }));

        Assert.Equal(2, result.Created);
        Assert.Equal(new[] { "EXAMINER", "MODERATOR" }, result.Letters.Select(l => l.Type));
    }

    [Fact]
    public async Task External_staff_can_be_appointed()
    {
        var external = Person("", "Dr.", "Neha", null, "Iyer", "neha@other.edu", external: true);
        _context.SaveChanges();

        var result = await Save("2026-10-20", Pick(_s5, new[] { external }));

        Assert.Equal("Dr. Neha Iyer", result.Letters.Single().StaffName);
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public async Task Staff_who_have_left_are_refused()
    {
        var left = Person("E104", "Mr.", "Om", null, "Das", "om@viva.edu", active: false);
        _context.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-20", Pick(_s5, new[] { left })));

        Assert.Equal("Mr. Om Das has left and cannot be appointed.", ex.Message);
        Assert.Empty(await _context.StaffLetters.ToListAsync());
    }

    [Fact]
    public async Task Someone_who_left_keeps_the_letter_they_hold_but_gets_no_new_one()
    {
        var leaving = Person("E105", "Mr.", "Jay", null, "Pal", "jay@viva.edu");
        _context.SaveChanges();
        var first = await Save("2026-10-20", Pick(_s5, new[] { leaving }));
        leaving.IsActive = false;
        _context.SaveChanges();

        var again = await Save("2026-10-21", Pick(_s5, new[] { leaving }));
        Assert.Equal(first.Letters.Single().LetterNo, again.Letters.Single().LetterNo);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-21", Pick(_s5, Array.Empty<StaffMaster>(), new[] { leaving })));
        Assert.Equal("Mr. Jay Pal has left and cannot be appointed.", ex.Message);
    }

    [Fact]
    public async Task The_same_person_cannot_be_examiner_and_moderator_of_one_subject_or_listed_twice()
    {
        var both = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-20", Pick(_s5, new[] { _asha }, new[] { _asha })));
        Assert.Equal("Prof. Asha R. Patil cannot be both examiner and moderator for S5-A Databases.", both.Message);

        var twice = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-20", Pick(_s5, new[] { _ravi, _ravi })));
        Assert.Equal("Dr. Ravi Kumar is listed more than once as examiner for S5-A Databases.", twice.Message);

        Assert.Empty(await _context.StaffLetters.ToListAsync());
    }

    [Fact]
    public async Task A_subject_that_is_not_on_this_exams_timetable_is_refused()
    {
        var elsewhere = Subject("X1", "Not timetabled", "Sem-5", _computer, 5);
        _context.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-20",
            Pick(_s5, new[] { _asha }), Pick(elsewhere, new[] { _ravi }),
            new AppointmentSubjectSave { SubjectId = Guid.NewGuid(), Examiners = new List<Guid> { _meena.StaffId } }));

        Assert.Contains("not on this exam's timetable", ex.Message);
        Assert.Empty(await _context.StaffLetters.ToListAsync());
    }

    [Fact]
    public async Task Unknown_staff_and_another_colleges_staff_are_refused()
    {
        var theirs = Person("E900", "Mr.", "Their", null, "Person", "their@other.edu", college: _otherCollege);
        _context.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-20",
            Pick(_s5, new[] { theirs }, new[] { new StaffMaster { StaffId = Guid.NewGuid(), Title = "Mr.", FirstName = "Nobody", Email = "x@y.z" } })));

        Assert.Equal("A member of staff in the list was not found.", ex.Message);
        Assert.Empty(await _context.StaffLetters.ToListAsync());
    }

    [Fact]
    public async Task The_letter_date_is_needed_only_for_new_letters_and_must_be_a_real_date()
    {
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("", Pick(_s5, new[] { _asha })));
        Assert.Equal("Enter the letter date.", missing.Message);

        var bad = await Assert.ThrowsAsync<InvalidOperationException>(() => Save("20/10/2026", Pick(_s5, new[] { _asha })));
        Assert.Contains("letter date is not a valid date", bad.Message);

        Assert.Empty(await _context.StaffLetters.ToListAsync());
    }

    [Fact]
    public async Task A_bad_screen_saves_nothing_and_leaves_the_existing_letters_as_they_were()
    {
        await Save("2026-10-20", Pick(_s5, new[] { _asha }, new[] { _ravi }));
        var left = Person("E104", "Mr.", "Om", null, "Das", "om@viva.edu", active: false);
        _context.SaveChanges();

        // Meena would be new and Ravi would be cancelled, but Om has left: nothing happens.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Save("2026-10-21",
            Pick(_s5, new[] { _asha, left }), Pick(_s6First, new[] { _meena })));

        var letters = await _context.StaffLetters.ToListAsync();
        Assert.Equal(2, letters.Count);
        Assert.All(letters, l => Assert.False(l.IsDeleted));
        Assert.Contains(letters, l => l.StaffId == _ravi.StaffId && l.LetterType == StaffLetterTypes.Moderator);
        Assert.DoesNotContain(letters, l => l.StaffId == _meena.StaffId);
    }

    // ------------------------------------------------------------------ tenancy

    [Fact]
    public async Task Another_colleges_exams_are_invisible()
    {
        var theirCourse = new CourseMaster { CourseId = Guid.NewGuid(), Name = "Mechanical", CourseCode = "MEC", CollegeId = _otherCollege };
        _context.CourseMasters.Add(theirCourse);
        var theirExam = new ExamMaster
        {
            ExamId = Guid.NewGuid(), Name = "December 2026", ExamType = "Regular", AcademicYearAYID = _ay, CourseId = theirCourse.CourseId,
            IsActive = true, CollegeId = _otherCollege
        };
        _context.Exams.Add(theirExam);
        _context.SaveChanges();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.AppointmentsAsync(theirExam.ExamId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SaveAppointmentsAsync(new AppointmentSave { ExamId = theirExam.ExamId, LetterDate = "2026-10-20" }));
    }
}
