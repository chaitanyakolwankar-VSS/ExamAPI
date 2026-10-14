using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Common;
using ExamAPI.Services.Exam;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>Exams are per branch: "December 2026 / Regular" for Civil must not block the same exam for Computer (live, 2026-10-14).</summary>
public sealed class ExamCreateTests
{
    private readonly ApplicationDbContext _context;
    private readonly ExamService _service;
    private readonly Guid _ay = Guid.NewGuid();
    private readonly Guid _civil = Guid.NewGuid();
    private readonly Guid _computer = Guid.NewGuid();

    public ExamCreateTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(Guid.NewGuid());
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new ExamService(_context, new GenericRepository(_context));
    }

    private Exams December(Guid branch) => new() { Courseid = branch, Name = "December 2026", ExamType = "Regular", Ayid = _ay };

    [Fact]
    public async Task The_same_exam_can_be_created_for_another_branch_but_not_twice_for_one_branch()
    {
        Assert.True((await _service.CreateExamAsync(December(_civil))).Success);

        Assert.False((await _service.SearchExam(December(_computer))).Success);  // "not found" for Computer
        Assert.True((await _service.CreateExamAsync(December(_computer))).Success);

        var again = await _service.CreateExamAsync(December(_civil));
        Assert.False(again.Success);
        Assert.Contains("already exists", again.Message);
        Assert.Equal(2, await _context.Exams.CountAsync());
    }
}
