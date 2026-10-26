using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.AttendenceReport;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// Attendance Report: the papers of an exam in timetable order, and who writes each one (same students as the
/// hall ticket, minus ATKT students only carrying the written head).
/// </summary>
public sealed class AttendenceReportTests
{
    private readonly ApplicationDbContext _context;
    private readonly AttendenceReportService _service;
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly CourseMaster _course;
    private readonly ExamMaster _exam;

    public AttendenceReportTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new AttendenceReportService(_context);

        _course = new CourseMaster { CourseId = Guid.NewGuid(), Name = "Computer", CourseCode = "CMP", CollegeId = _college };
        _context.CourseMasters.Add(_course);
        _exam = Exam("December 2026");
    }

    private ExamMaster Exam(string name, Guid? revalFor = null)
    {
        var e = new ExamMaster
        {
            ExamId = Guid.NewGuid(), Name = name, ExamType = "Regular", AcademicYearAYID = _ay, CourseId = _course.CourseId,
            RevaluationForExamId = revalFor, IsActive = true, CollegeId = _college
        };
        _context.Exams.Add(e);
        return e;
    }

    // A subject with a written head H1 (ESE) and an internal head H2 (IA), timetabled for the exam.
    private (SubjectMaster Subject, Guid Credits) Paper(string code, string date, string time = "10:00 AM - 01:00 PM", string sem = "Sem-5",
        int? order = null)
    {
        var s = new SubjectMaster
        {
            SubjectId = Guid.NewGuid(), SubjectCode = code, Name = code + " name", SemId = sem, Pattern = "NEP",
            SortOrder = order, CourseId = _course.CourseId, CollegeId = _college
        };
        var credits = Guid.NewGuid();
        _context.SubjectMasters.Add(s);
        _context.SubjectCredits.Add(new SubjectCredits { Id = Guid.NewGuid(), CreditsId = credits, Head = "H1", HeadType = "ESE" });
        _context.SubjectCredits.Add(new SubjectCredits { Id = Guid.NewGuid(), CreditsId = credits, Head = "H2", HeadType = "IA" });
        _context.TimeTables.Add(new TimeTableMaster
        {
            Id = Guid.NewGuid(), ExamId = _exam.ExamId, CourseId = _course.CourseId, SubjectId = s.SubjectId, Date = date, Time = time,
            CollegeId = _college
        });
        return (s, credits);
    }

    private MarksMaster Student(string? seat, Guid? college = null)
    {
        var m = new MarksMaster
        {
            MarksId = Guid.NewGuid(), ExamId = _exam.ExamId, AcademicYearAYID = _ay, SemesterId = "Sem-5", Pattern = "NEP",
            SeatNo = seat, StudentID = "S" + seat, CollegeId = college ?? _college
        };
        _context.MarksMasters.Add(m);
        return m;
    }

    private void Head(MarksMaster m, (SubjectMaster Subject, Guid Credits) paper, string head, bool carried = false)
        => _context.StudentMarks.Add(new StudentMarks
        {
            Id = Guid.NewGuid(), MarksId = m.MarksId, SubjectId = paper.Subject.SubjectId, CreditsId = paper.Credits, Head = head,
            IsCarryForward = carried
        });

    private void Writes(MarksMaster m, (SubjectMaster Subject, Guid Credits) paper)
    {
        Head(m, paper, "H1");
        Head(m, paper, "H2");
    }

    private AttendenceSeatNoRequestDTO Request(Guid? subject = null) => new()
    {
        Ayid = _ay, CourseId = _course.CourseId, Pattern = "NEP", Semester = "Sem-5", Exam = _exam.ExamId,
        SubjectId = subject ?? Guid.Empty
    };

    [Fact]
    public async Task Papers_come_in_timetable_order_for_the_chosen_semester_only()
    {
        var january = Paper("CS503", "05-01-2027");
        var december = Paper("CS501", "30-12-2026");
        var sameSlotLater = Paper("CS502", "30-12-2026", order: 2);
        var sameSlotFirst = Paper("CS509", "30-12-2026", order: 1);
        var afternoon = Paper("CS504", "30-12-2026", time: "02:00 PM - 05:00 PM");
        Paper("CS701", "01-12-2026", sem: "Sem-7");
        Paper("CS505", "");
        await _context.SaveChangesAsync();

        var rows = await _service.GetSubjectStudentCount(Request());

        Assert.Equal(new[] { "CS509", "CS502", "CS501", "CS504", "CS503" }, rows.Select(r => r.SubjectCode));
        Assert.All(rows, r => Assert.Equal(0, r.StudentCount));
        Assert.Equal(january.Subject.SubjectId, rows[^1].SubjectId);
        Assert.Contains(rows, r => r.SubjectId == december.Subject.SubjectId && r.Date == "30-12-2026" && r.Time == "10:00 AM - 01:00 PM");
        Assert.Contains(rows, r => r.SubjectId == afternoon.Subject.SubjectId);
        Assert.Contains(rows, r => r.SubjectId == sameSlotLater.Subject.SubjectId);
        Assert.Contains(rows, r => r.SubjectId == sameSlotFirst.Subject.SubjectId);
    }

    [Fact]
    public async Task Only_students_with_a_seat_who_write_the_paper_are_counted_and_seats_come_in_order()
    {
        var paper = Paper("CS501", "01-12-2026");
        var other = Paper("CS502", "02-12-2026");

        Writes(Student("1002"), paper);
        Writes(Student("999"), paper);
        Writes(Student("1001"), paper);
        Writes(Student(null), paper);                          // no seat number yet
        Writes(Student(""), paper);
        var atkt = Student("1003");                            // ATKT: carries the written head, re-sits only the internal
        Head(atkt, paper, "H1", carried: true);
        Head(atkt, paper, "H2");
        Writes(atkt, other);
        var foreign = Student("1004", _otherCollege);          // another college's student
        Writes(foreign, paper);
        await _context.SaveChangesAsync();

        var rows = await _service.GetSubjectStudentCount(Request());
        var seats = await _service.GetSubjectSeatNo(Request(paper.Subject.SubjectId));

        Assert.Equal(3, rows.Single(r => r.SubjectCode == "CS501").StudentCount);
        Assert.Equal(1, rows.Single(r => r.SubjectCode == "CS502").StudentCount);
        Assert.Equal(new[] { "999", "1001", "1002" }, seats.Select(s => s.SeatNo));
        Assert.Equal(new[] { "1003" }, (await _service.GetSubjectSeatNo(Request(other.Subject.SubjectId))).Select(s => s.SeatNo));
    }

    [Fact]
    public async Task Exam_list_leaves_out_revaluation_exams()
    {
        Exam("May 2027");
        Exam("December 2026 revaluation", revalFor: _exam.ExamId);
        await _context.SaveChangesAsync();

        var exams = await _service.GetExam(new ReportExamRequest { Ayid = _ay, CourseId = _course.CourseId });

        Assert.Equal(new[] { "December 2026 Regular", "May 2027 Regular" }, exams.Select(e => e.Examname));
    }
}
