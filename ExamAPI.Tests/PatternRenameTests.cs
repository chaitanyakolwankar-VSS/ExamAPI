using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Ordinance;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>
/// Subjects, enrolments, marks and declared results store the pattern by name. Renaming a pattern must carry
/// the new name to all of them (in the caller's college only), or they vanish from every list (live, 2026-10-14).
/// </summary>
public sealed class PatternRenameTests
{
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly Guid _patternId = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly OrdinanceService _service;

    public PatternRenameTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);

        _context.PatternMasters.AddRange(
            new PatternMaster { PatternId = _patternId, PatternName = "NEP", CollegeId = _college },
            new PatternMaster { PatternId = Guid.NewGuid(), PatternName = "NEP - R-26", CollegeId = _college },
            new PatternMaster { PatternId = Guid.NewGuid(), PatternName = "NEP", CollegeId = _other });
        foreach (var college in new[] { _college, _other })
        {
            _context.SubjectMasters.Add(new SubjectMaster { SubjectId = Guid.NewGuid(), SubjectCode = "S1", Name = "Maths", Pattern = "NEP", CollegeId = college });
            _context.StudentEligibilities.Add(new StudentEligibility { Id = Guid.NewGuid(), StudentId = "2600001", Pattern = "NEP", CollegeId = college });
            _context.MarksMasters.Add(new MarksMaster { MarksId = Guid.NewGuid(), StudentID = "2600001", Pattern = "NEP", CollegeId = college });
        }
        _context.SaveChanges();
        _service = new OrdinanceService(_context, new EngineRegistry(Array.Empty<IFactProvider>(), Array.Empty<IActionHandler>()));
    }

    [Fact]
    public async Task Renaming_a_pattern_moves_its_subjects_enrolments_and_marks_in_this_college_only()
    {
        Assert.True(await _service.UpdatePatternAsync(new PatternUpdateDto { PatternId = _patternId, PatternName = " NEP - R-24 ", Description = "" }));

        _context.ChangeTracker.Clear();
        Assert.Equal("NEP - R-24", (await _context.PatternMasters.SingleAsync(p => p.PatternId == _patternId)).PatternName);
        Assert.All(await _context.SubjectMasters.IgnoreQueryFilters().Where(s => s.CollegeId == _college).ToListAsync(), s => Assert.Equal("NEP - R-24", s.Pattern));
        Assert.All(await _context.StudentEligibilities.IgnoreQueryFilters().Where(s => s.CollegeId == _college).ToListAsync(), s => Assert.Equal("NEP - R-24", s.Pattern));
        Assert.All(await _context.MarksMasters.IgnoreQueryFilters().Where(s => s.CollegeId == _college).ToListAsync(), s => Assert.Equal("NEP - R-24", s.Pattern));
        // Another college with a pattern of the same old name is untouched.
        Assert.Equal("NEP", (await _context.SubjectMasters.IgnoreQueryFilters().SingleAsync(s => s.CollegeId == _other)).Pattern);
        Assert.Equal("NEP", (await _context.MarksMasters.IgnoreQueryFilters().SingleAsync(s => s.CollegeId == _other)).Pattern);
    }

    [Fact]
    public async Task Renaming_to_a_name_the_college_already_has_is_refused_and_changes_nothing()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdatePatternAsync(new PatternUpdateDto { PatternId = _patternId, PatternName = "NEP - R-26", Description = "" }));
        Assert.Contains("already exists", ex.Message);

        _context.ChangeTracker.Clear();
        Assert.Equal("NEP", (await _context.PatternMasters.SingleAsync(p => p.PatternId == _patternId)).PatternName);
        Assert.Equal("NEP", (await _context.SubjectMasters.SingleAsync()).Pattern);
    }
}
