using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.RegularExam;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>
/// Live, 2026-10-14: Save pressed twice on assignment gave 72 Electrical students every head twice,
/// so marks entry showed 60, 40, 60, 40. Assignment now never gives a student a head they already have.
/// </summary>
public sealed class RegularExamAssignTests
{
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly Guid _exam = Guid.NewGuid();
    private readonly Guid _subject = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly RegularExamService _service;

    public RegularExamAssignTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new RegularExamService(_context, new GenericRepository(_context));

        _context.SubjectMasters.Add(new SubjectMaster { SubjectId = _subject, SubjectCode = "MA101", Name = "Mathematics", Pattern = "NEP", CollegeId = _college });
        _context.StudentMasters.Add(new StudentMaster { StdMstId = _student, StudentId = "2600001", FirstName = "Ravi", LastName = "Patil", CollegeId = _college });
        AddCredits();
        _context.SaveChanges();
    }

    private void AddCredits()
    {
        var creditsId = Guid.NewGuid();
        _context.SubjectCreditMasters.Add(new SubjectCreditMaster { CreditsId = creditsId, SubjectId = _subject, AYID = _ay.ToString(), TotalCredits = "2", CollegeId = _college });
        _context.SubjectCredits.AddRange(
            new SubjectCredits { Id = Guid.NewGuid(), CreditsId = creditsId, Head = "H1", HeadOutOf = "60", HeadPass = "24" },
            new SubjectCredits { Id = Guid.NewGuid(), CreditsId = creditsId, Head = "H2", HeadOutOf = "40", HeadPass = "16" });
    }

    private SaveRegularExamStudentsDto Assign() => new()
    {
        ExamInfo = new RegularExamStudents { CourseId = Guid.NewGuid(), Pattern = "NEP", Semester = "Sem-1", ExamId = _exam, SubjectId = [_subject], Ayid = _ay },
        Students = [new RegularStudents { StdMstId = _student, StudentId = "2600001", StudentName = "PATIL RAVI", Assigned = true }]
    };

    [Fact]
    public async Task A_student_gets_each_head_once_even_if_save_is_pressed_twice()
    {
        Assert.True((await _service.SaveRegularExamStudents(Assign())).Success);
        Assert.True((await _service.SaveRegularExamStudents(Assign())).Success);

        Assert.Equal(new[] { "H1", "H2" }, _context.StudentMarks.Select(m => m.Head).OrderBy(h => h).ToArray());
        Assert.Single(_context.MarksMasters);
    }

    [Fact]
    public async Task A_subject_with_two_credit_entries_gets_both_once_each()
    {
        // e.g. MCA11 on live: a 50 + 50 entry and a separate 25 entry, saved together.
        AddCredits();
        _context.SaveChanges();

        Assert.True((await _service.SaveRegularExamStudents(Assign())).Success);
        Assert.True((await _service.SaveRegularExamStudents(Assign())).Success);

        Assert.Equal(4, _context.StudentMarks.Count());
        Assert.Equal(2, _context.StudentMarks.Select(m => m.CreditsId).Distinct().Count());
    }
}
