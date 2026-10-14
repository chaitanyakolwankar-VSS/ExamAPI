using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.GenerateHallTicket;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// Owner decision 2026-10-14: "Print time tables" reads every branch's time table of an exam.
/// </summary>
public sealed class GenerateHallTicketTimeTableTests
{
    private readonly ApplicationDbContext _context;
    private readonly GenerateHallTicketService _service;
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly Guid _otherAy = Guid.NewGuid();

    public GenerateHallTicketTimeTableTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new GenerateHallTicketService(_context);
    }

    private CourseMaster Course(string name, Guid? college = null)
    {
        var c = new CourseMaster { CourseId = Guid.NewGuid(), Name = name, CourseCode = name[..3].ToUpper(), CollegeId = college ?? _college };
        _context.CourseMasters.Add(c);
        return c;
    }

    private ExamMaster Exam(CourseMaster course, string name = "December 2026", string type = "Regular", Guid? ay = null,
        Guid? revalFor = null, Guid? college = null)
    {
        var e = new ExamMaster
        {
            ExamId = Guid.NewGuid(), Name = name, ExamType = type, AcademicYearAYID = ay ?? _ay, CourseId = course.CourseId,
            RevaluationForExamId = revalFor, CollegeId = college ?? _college
        };
        _context.Exams.Add(e);
        return e;
    }

    private SubjectMaster Subject(CourseMaster course, string code, string name, int? order = null, string sem = "Sem-3",
        string pattern = "NEP", Guid? college = null)
    {
        var s = new SubjectMaster
        {
            SubjectId = Guid.NewGuid(), SubjectCode = code, Name = name, SortOrder = order, SemId = sem, Pattern = pattern,
            CourseId = course.CourseId, CollegeId = college ?? _college
        };
        _context.SubjectMasters.Add(s);
        return s;
    }

    private static HallTicketSubjects Row(SubjectMaster s, string date, string time)
        => new() { SubjectId = s.SubjectId, SubjectCode = s.SubjectCode, SubjectName = s.Name, ExamDate = date, ExamTime = time };

    private Task<ApiResponseDto<object>> Save(ExamMaster exam, CourseMaster course, params HallTicketSubjects[] rows)
        => _service.SaveTimeTable(new SaveTimeTable
        {
            ExamId = exam.ExamId, CourseId = course.CourseId, TimeTableData = rows.ToList()
        });

    [Fact]
    public async Task Timetable_all_returns_a_block_per_branch_and_semester_in_subject_order()
    {
        var mech = Course("Mechanical"); var civil = Course("Civil"); var empty = Course("Chemical");
        var examM = Exam(mech); var examC = Exam(civil); Exam(empty);
        var foreign = Course("Foreign", _otherCollege);
        var foreignExam = Exam(foreign, college: _otherCollege);
        var late = Subject(civil, "CE305", "Late", order: 2, sem: "Sem-3");
        var first = Subject(civil, "CE399", "First", order: 1, sem: "Sem-3");
        var unnumbered = Subject(civil, "CE301", "Unnumbered", sem: "Sem-3");
        var civil5 = Subject(civil, "CE501", "Sem five", sem: "Sem-5");
        var civilNoTt = Subject(civil, "CE302", "Not scheduled", sem: "Sem-3");
        var mech3 = Subject(mech, "ME301", "Mech three", sem: "Sem-3");
        var foreignSubject = Subject(foreign, "FF301", "Foreign", college: _otherCollege);
        await _context.SaveChangesAsync();
        await Save(examC, civil, Row(late, "04-12-2026", "10 AM"), Row(first, "03-12-2026", "10 AM"),
            Row(unnumbered, "05-12-2026", "10 AM"), Row(civil5, "06-12-2026", "10 AM"), Row(civilNoTt, "", ""));
        await Save(examM, mech, Row(mech3, "03-12-2026", "2 PM"));
        _context.TimeTables.Add(new TimeTableMaster { Id = Guid.NewGuid(), ExamId = foreignExam.ExamId, CourseId = foreign.CourseId, SubjectId = foreignSubject.SubjectId, Date = "01-01-2026", Time = "x", CollegeId = _otherCollege });
        _context.AcademicYears.Add(new AcademicYear { AYID = _ay, FullDuration = "01/June/2026-31/May/2027", ShortDuration = "2026-2027", CollegeId = _college });
        await _context.SaveChangesAsync();

        // Asking from any branch's copy returns the same, all-branch result.
        var dto = await _service.GetTimeTableAll(examM.ExamId);

        Assert.NotNull(dto);
        Assert.Equal("December 2026", dto!.ExamName);
        Assert.Equal("Regular", dto.ExamType);
        Assert.Equal("2026-2027", dto.AcademicYear);
        Assert.Equal(new[] { "Civil|NEP|Semester III", "Civil|NEP|Semester V", "Mechanical|NEP|Semester III" },
            dto.Blocks.Select(b => $"{b.BranchName}|{b.Pattern}|{b.Semester}"));
        Assert.Equal(new[] { "CE399", "CE305", "CE301" }, dto.Blocks[0].Rows.Select(r => r.SubjectCode));
        Assert.Equal("03-12-2026", dto.Blocks[0].Rows[0].Date);
        Assert.Equal("10 AM", dto.Blocks[0].Rows[0].Time);
        Assert.Equal("First", dto.Blocks[0].Rows[0].SubjectName);
        Assert.DoesNotContain(dto.Blocks.SelectMany(b => b.Rows), r => r.SubjectCode is "CE302" or "FF301");

        Assert.Null(await _service.GetTimeTableAll(Guid.NewGuid()));
    }
}
