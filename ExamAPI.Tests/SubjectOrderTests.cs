using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Lookup;
using ExamAPI.Services.Report;
using ExamAPI.Services.Result;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.StudentAssignRpt;
using ExamAPI.Services.Subject;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// Owner decision 2026-10-14: every subject has a staff-set order number (Subject Master) and every
/// list of subjects follows it: SortOrder ascending, NULLs last, then subject code, then name.
/// </summary>
public sealed class SubjectOrderTests
{
    private const string Sem = "Sem-6";
    private const string Pattern = "NEP";

    private readonly ApplicationDbContext _context;
    private readonly SubjectService _subjects;
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly Guid _course = Guid.NewGuid();

    public SubjectOrderTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _subjects = new SubjectService(_context, new GenericRepository(_context));
    }

    private SubjectMaster Subject(string code, int? order, string? name = null, Guid? college = null,
        Guid? course = null, string sem = Sem, string pattern = Pattern)
    {
        var s = new SubjectMaster
        {
            SubjectId = Guid.NewGuid(), SubjectCode = code, Name = name ?? "Name " + code, SortOrder = order,
            CollegeId = college ?? _college, CourseId = course ?? _course, SemId = sem, Pattern = pattern
        };
        _context.SubjectMasters.Add(s);
        return s;
    }

    private Task<ApiResponseDto<object>> Create(string code, Guid? course = null, string sem = Sem, string pattern = Pattern)
        => _subjects.CreateSubjectAsync(new CreateSubjectDto
        {
            SubjectName = "Name " + code, SubjectCode = code, SemId = sem, SemName = sem,
            Pattern = pattern, CourseID = course ?? _course
        });

    // ---------------------------------------------------------------- the ordering helper

    [Fact]
    public void InMemory_order_puts_numbered_subjects_first_ties_by_code_nulls_last()
    {
        var rows = new[]
        {
            (Order: (int?)null, Code: "B1", Name: "x"),
            (Order: (int?)2, Code: "Z9", Name: "x"),
            (Order: (int?)null, Code: "a1", Name: "x"),
            (Order: (int?)2, Code: "c3", Name: "x"),
            (Order: (int?)1, Code: "Z0", Name: "x"),
        };

        var ordered = rows.InSubjectOrder(r => r.Order, r => r.Code, r => r.Name).Select(r => r.Code).ToList();

        // 1 first; the two 2s tie and fall back to code (ignoring case); the NULLs last, also by code.
        Assert.Equal(new[] { "Z0", "c3", "Z9", "a1", "B1" }, ordered);
    }

    [Fact]
    public void InMemory_order_falls_back_to_name_when_order_and_code_tie()
    {
        var rows = new[] { (Order: (int?)1, Code: "A", Name: "Beta"), (Order: (int?)1, Code: "a", Name: "alpha") };

        var ordered = rows.InSubjectOrder(r => r.Order, r => r.Code, r => r.Name).Select(r => r.Name).ToList();

        Assert.Equal(new[] { "alpha", "Beta" }, ordered);
    }

    [Fact]
    public void Compare_agrees_with_the_enumerable_order()
    {
        Assert.True(SubjectOrder.Compare(1, "Z", "n", null, "A", "n") < 0);
        Assert.True(SubjectOrder.Compare(null, "A", "n", 5, "Z", "n") > 0);
        Assert.True(SubjectOrder.Compare(3, "a", "n", 3, "B", "n") < 0);
        Assert.Equal(0, SubjectOrder.Compare(null, "a", "n", null, "A", "N"));
    }

    [Fact]
    public async Task Query_order_matches_the_rule()
    {
        Subject("B1", null); Subject("Z9", 2); Subject("A1", null); Subject("C3", 2); Subject("Z0", 1);
        await _context.SaveChangesAsync();

        var codes = await _context.SubjectMasters.InSubjectOrder().Select(s => s.SubjectCode).ToListAsync();

        Assert.Equal(new[] { "Z0", "C3", "Z9", "A1", "B1" }, codes);
    }

    // ---------------------------------------------------------------- create / set-order

    [Fact]
    public async Task CreateSubject_numbers_new_subjects_after_the_last_one_of_the_same_course_pattern_and_semester()
    {
        Assert.True((await Create("S1")).Success);
        Assert.True((await Create("S2")).Success);
        Subject("OTHER-COLLEGE", 50, college: _otherCollege);                 // other tenant: ignored
        var otherCourse = Guid.NewGuid();
        Subject("OTHER-COURSE", 40, course: otherCourse);                     // other branch: ignored
        await _context.SaveChangesAsync();
        Assert.True((await Create("S3")).Success);
        Assert.True((await Create("OC1", course: otherCourse)).Success);       // continues that branch: 41
        Assert.True((await Create("P1", pattern: "CBCS")).Success);            // new pattern: starts at 1
        Assert.True((await Create("SEM5", sem: "Sem-5")).Success);             // new semester: starts at 1

        int? Order(string code) => _context.SubjectMasters.IgnoreQueryFilters().Single(s => s.SubjectCode == code).SortOrder;
        Assert.Equal(1, Order("S1"));
        Assert.Equal(2, Order("S2"));
        Assert.Equal(3, Order("S3"));
        Assert.Equal(41, Order("OC1"));
        Assert.Equal(1, Order("P1"));
        Assert.Equal(1, Order("SEM5"));
    }

    [Fact]
    public async Task SetOrder_saves_the_numbers_and_reports_how_many()
    {
        var a = Subject("A", 1); var b = Subject("B", 2); var c = Subject("C", 3);
        await _context.SaveChangesAsync();

        var result = await _subjects.SetSubjectOrderAsync(new SetSubjectOrderDto
        {
            Subjects = new()
            {
                new() { SubjectId = a.SubjectId, SortOrder = 3 },
                new() { SubjectId = b.SubjectId, SortOrder = 1 },
                new() { SubjectId = c.SubjectId, SortOrder = 1 },   // duplicate numbers are allowed
            }
        });

        Assert.True(result.Success);
        Assert.Equal("Order saved for 3 subjects.", result.Message);
        var codes = await _context.SubjectMasters.InSubjectOrder().Select(s => s.SubjectCode).ToListAsync();
        Assert.Equal(new[] { "B", "C", "A" }, codes);   // B and C tie, so code decides
    }

    [Fact]
    public async Task SetOrder_refuses_another_colleges_subject_and_changes_nothing()
    {
        var mine = Subject("MINE", 1);
        var theirs = Subject("THEIRS", 7, college: _otherCollege);
        await _context.SaveChangesAsync();

        var result = await _subjects.SetSubjectOrderAsync(new SetSubjectOrderDto
        {
            Subjects = new()
            {
                new() { SubjectId = mine.SubjectId, SortOrder = 9 },
                new() { SubjectId = theirs.SubjectId, SortOrder = 1 },
            }
        });

        Assert.False(result.Success);
        Assert.Equal(1, _context.SubjectMasters.IgnoreQueryFilters().Single(s => s.SubjectCode == "MINE").SortOrder);
        Assert.Equal(7, _context.SubjectMasters.IgnoreQueryFilters().Single(s => s.SubjectCode == "THEIRS").SortOrder);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(1000)]
    public async Task SetOrder_refuses_numbers_outside_1_to_999(int number)
    {
        var s = Subject("A", 5);
        await _context.SaveChangesAsync();

        var result = await _subjects.SetSubjectOrderAsync(new SetSubjectOrderDto
        {
            Subjects = new() { new() { SubjectId = s.SubjectId, SortOrder = number } }
        });

        Assert.False(result.Success);
        Assert.Equal(5, _context.SubjectMasters.Single().SortOrder);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    public async Task SetOrder_accepts_the_range_limits(int number)
    {
        var s = Subject("A", 5);
        await _context.SaveChangesAsync();

        var result = await _subjects.SetSubjectOrderAsync(new SetSubjectOrderDto
        {
            Subjects = new() { new() { SubjectId = s.SubjectId, SortOrder = number } }
        });

        Assert.True(result.Success);
        Assert.Equal(number, _context.SubjectMasters.Single().SortOrder);
    }

    [Fact]
    public async Task SetOrder_refuses_an_empty_list()
    {
        var result = await _subjects.SetSubjectOrderAsync(new SetSubjectOrderDto());
        Assert.False(result.Success);
    }

    // ---------------------------------------------------------------- lookup

    [Fact]
    public async Task Lookup_subjects_come_back_in_subject_order_with_their_sortOrder()
    {
        Subject("AAA", 3); Subject("BBB", 1); Subject("CCC", null); Subject("DDD", 2);
        Subject("ZZZ-OTHER", 1, college: _otherCollege);
        await _context.SaveChangesAsync();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        var lookup = new LookupService(_context, currentUser.Object);

        var list = await lookup.GetSubjectsAsync(_course, Pattern, Sem);

        Assert.Equal(new[] { "BBB", "DDD", "AAA", "CCC" }, list.Select(s => s.Code));
        Assert.Equal(new int?[] { 1, 2, 3, null }, list.Select(s => s.SortOrder));
    }

    // ---------------------------------------------------------------- reports

    [Fact]
    public void Marksheet_subjects_follow_SortOrder_not_subject_code()
    {
        SubjectMarksDto Dto(string code, int? order) => new() { SubjectCode = code, SubjectName = code, SortOrder = order };

        var sorted = ReportService.SortBySubjectOrder(new[] { Dto("A-LAST", 3), Dto("Z-FIRST", 1), Dto("M-NONE", null), Dto("B-SECOND", 2) });

        Assert.Equal(new[] { "Z-FIRST", "B-SECOND", "A-LAST", "M-NONE" }, sorted.Select(s => s.SubjectCode));
    }

    [Fact]
    public void Printed_subject_carries_the_subjects_SortOrder()
    {
        var subject = Subject("S1", 4);
        var mark = new StudentMarks { Id = Guid.NewGuid(), Head = "H1", Marks = 10, Subject = subject, SubjectId = subject.SubjectId };

        var dto = ReportService.BuildSubjectMarks(new[] { mark }, null);

        Assert.Equal(4, dto!.SortOrder);
    }

    // ---------------------------------------------------------------- results grid

    private (ExamMaster exam, MarksMaster mm) SeedMarks(string studentId, params (SubjectMaster subject, string head)[] marks)
    {
        var exam = _context.Exams.Local.FirstOrDefault() ?? _context.Exams.Add(new ExamMaster
        {
            ExamId = Guid.NewGuid(), Name = "Regular", ExamType = "Regular", IsActive = true, CourseId = _course, CollegeId = _college
        }).Entity;
        var student = new StudentMaster
        {
            StdMstId = Guid.NewGuid(), StudentId = studentId, FirstName = "F", LastName = "L", CollegeId = _college
        };
        _context.StudentMasters.Add(student);
        var mm = new MarksMaster
        {
            MarksId = Guid.NewGuid(), StudentID = studentId, StdMstId = student.StdMstId, ExamId = exam.ExamId,
            SemesterId = Sem, Pattern = Pattern, CollegeId = _college, SeatNo = "S" + studentId
        };
        _context.MarksMasters.Add(mm);
        foreach (var (subject, head) in marks)
            _context.StudentMarks.Add(new StudentMarks
            {
                Id = Guid.NewGuid(), MarksId = mm.MarksId, SubjectId = subject.SubjectId, Head = head, Marks = 10, RawMarks = 10
            });
        return (exam, mm);
    }

    [Fact]
    public async Task Results_grid_subjectOrder_follows_SortOrder_with_heads_together()
    {
        var late = Subject("AAA", 3, "Late");
        var early = Subject("ZZZ", 1, "Early");
        var none = Subject("MMM", null, "Unnumbered");
        var (exam, _) = SeedMarks("ST1", (late, "H1"), (early, "H2"), (early, "H1"), (none, "H1"));
        SeedMarks("ST2", (late, "H1"), (early, "H1"));
        await _context.SaveChangesAsync();
        var results = new ResultService(_context, new EngineRegistry(Array.Empty<IFactProvider>(), Array.Empty<IActionHandler>()));

        var response = await results.GetResultsAsync(new ProcessResultRequest
        {
            BranchId = _course, ExamId = exam.ExamId, SemId = Sem, Pattern = Pattern
        }, _college);

        Assert.True(response.Success);
        Assert.Equal(new[]
        {
            "ZZZ - Early (H1)", "ZZZ - Early (H2)",
            "AAA - Late (H1)",
            "MMM - Unnumbered (H1)"
        }, response.SubjectOrder);
        // Every order key is a real key of the rows' SubjectMarks dictionaries.
        var allKeys = response.Data!.SelectMany(r => r.SubjectMarks.Keys).Distinct().OrderBy(k => k).ToList();
        Assert.Equal(allKeys, response.SubjectOrder.OrderBy(k => k).ToList());
    }

    // ---------------------------------------------------------------- Student Assign report

    [Fact]
    public async Task Student_assign_report_is_ordered_by_student_then_subject_order()
    {
        var first = Subject("ZZZ", 1); var second = Subject("AAA", 2); var unnumbered = Subject("BBB", null);
        var (exam, _) = SeedMarks("ST1", (unnumbered, "H1"), (second, "H1"), (first, "H1"));
        SeedMarks("ST2", (second, "H1"), (first, "H1"));
        await _context.SaveChangesAsync();
        var service = new StudentAssignRptService(_context);

        var rows = await service.GetReportAsync(new StudentAssignRptDto
        {
            CourseId = _course, Pattern = Pattern, Semester = Sem, ExamId = exam.ExamId
        });

        // StudentID descending, and within a student the subject order.
        Assert.Equal(new[] { "ST2:ZZZ", "ST2:AAA", "ST1:ZZZ", "ST1:AAA", "ST1:BBB" },
            rows.Select(r => $"{r.StudentID}:{r.SubjectCode}"));
    }
}
