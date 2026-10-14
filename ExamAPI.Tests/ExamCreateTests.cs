using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Exam;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>One Save on Exam Master creates the exam for every branch of the caller's college (owner decision, 2026-10-14).</summary>
public sealed class ExamCreateTests
{
    private readonly ApplicationDbContext _context;
    private readonly ExamService _service;
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly Guid _civil = Guid.NewGuid();
    private readonly Guid _computer = Guid.NewGuid();
    private readonly Guid _mech = Guid.NewGuid();
    private readonly Guid _otherBranch = Guid.NewGuid();

    public ExamCreateTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new ExamService(_context, new GenericRepository(_context));

        _context.CourseMasters.AddRange(
            Branch(_civil, "B.Tech. Civil Engineering", "CIV", _college),
            Branch(_computer, "B.Tech. Computer Engineering", "CSE", _college),
            Branch(_mech, "B.Tech. Mechanical Engineering", "MEC", _college),
            Branch(_otherBranch, "Other College Branch", "OTH", _otherCollege));
        _context.SaveChanges();
    }

    private static CourseMaster Branch(Guid id, string name, string code, Guid college) =>
        new() { CourseId = id, Name = name, CourseCode = code, CollegeId = college };

    private Exams December(Guid branch, bool reval = false) =>
        new() { Courseid = branch, Name = "December 2026", ExamType = "Regular", Ayid = _ay, RevalExam = reval };

    private Task<List<ExamMaster>> OwnExams() => _context.Exams.Where(e => e.RevaluationForExamId == null).ToListAsync();

    [Fact]
    public async Task One_save_creates_the_exam_for_every_branch_of_the_college()
    {
        var res = await _service.CreateExamAsync(December(_civil));

        Assert.True(res.Success);
        Assert.Equal("Exam created for 3 branches.", res.Message);
        var exams = await OwnExams();
        Assert.Equal(new Guid?[] { _civil, _computer, _mech }.OrderBy(x => x).ToList(), exams.Select(e => e.CourseId).OrderBy(x => x).ToList());
        Assert.All(exams, e => Assert.False(e.IsActive));
        Assert.Equal(3, await _context.Exams.CountAsync());
    }

    [Fact]
    public async Task Revaluation_twins_are_created_per_branch_and_linked_to_that_branchs_exam()
    {
        Assert.True((await _service.CreateExamAsync(December(_civil, reval: true))).Success);

        var all = await _context.Exams.ToListAsync();
        Assert.Equal(6, all.Count);
        foreach (var twin in all.Where(e => e.RevaluationForExamId != null))
        {
            var original = all.Single(e => e.ExamId == twin.RevaluationForExamId);
            Assert.Equal(original.CourseId, twin.CourseId);
            Assert.Null(original.RevaluationForExamId);
        }
        Assert.Equal(3, all.Count(e => e.RevaluationForExamId != null));
    }

    [Fact]
    public async Task A_second_save_creates_nothing_and_says_it_already_exists()
    {
        await _service.CreateExamAsync(December(_civil, reval: true));

        var again = await _service.CreateExamAsync(December(_computer, reval: true));

        Assert.False(again.Success);
        Assert.Equal("Exam already exists for every branch.", again.Message);
        Assert.Equal(6, await _context.Exams.CountAsync());
        Assert.True((await _service.SearchExam(December(_civil))).Success);
    }

    [Fact]
    public async Task A_new_branch_gets_the_exam_on_the_next_save_and_existing_branches_are_left_alone()
    {
        await _service.CreateExamAsync(December(_civil));
        _context.CourseMasters.Add(Branch(Guid.NewGuid(), "B.Tech. Electrical Engineering", "ELE", _college));
        await _context.SaveChangesAsync();

        Assert.False((await _service.SearchExam(December(_civil))).Success);  // one branch is still missing, so Save is allowed
        var res = await _service.CreateExamAsync(December(_civil));

        Assert.True(res.Success);
        Assert.Equal("Exam created for 1 branch; already existed for B.Tech. Civil Engineering, B.Tech. Computer Engineering, B.Tech. Mechanical Engineering.", res.Message);
        Assert.Equal(4, await _context.Exams.CountAsync());
        Assert.True((await _service.SearchExam(December(_civil))).Success);
    }

    [Fact]
    public async Task Another_colleges_branches_are_untouched()
    {
        await _service.CreateExamAsync(December(_civil));

        var other = await _context.Exams.IgnoreQueryFilters().Where(e => e.CourseId == _otherBranch).ToListAsync();
        Assert.Empty(other);
        Assert.Equal(3, await _context.Exams.IgnoreQueryFilters().CountAsync());
    }
}
