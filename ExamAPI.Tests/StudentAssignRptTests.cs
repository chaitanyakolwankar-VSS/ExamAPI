using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.StudentAssignRpt;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>T-59: the Student Assign Report lists each student's own subjects, not every subject of the semester.</summary>
public sealed class StudentAssignRptTests
{
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _course = Guid.NewGuid();
    private readonly Guid _exam = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly StudentAssignRptService _service;

    public StudentAssignRptTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new StudentAssignRptService(_context);
    }

    private (Guid SubjectId, Guid CreditsId) Subject(string code, int heads, string? ayid = null)
    {
        var subject = new SubjectMaster { SubjectId = Guid.NewGuid(), SubjectCode = code, Name = code, CourseId = _course, Pattern = "NEP", SemId = "Sem-6", CollegeId = _college };
        var credit = new SubjectCreditMaster { CreditsId = Guid.NewGuid(), SubjectId = subject.SubjectId, TotalCredits = "4", AYID = ayid ?? Guid.NewGuid().ToString(), CollegeId = _college };
        _context.SubjectMasters.Add(subject);
        _context.SubjectCreditMasters.Add(credit);
        for (var h = 1; h <= heads; h++)
            _context.SubjectCredits.Add(new SubjectCredits { Id = Guid.NewGuid(), CreditsId = credit.CreditsId, Head = $"H{h}", HeadType = h == 1 ? "ESE" : "IA", HeadOutOf = "50", HeadPass = "20" });
        return (subject.SubjectId, credit.CreditsId);
    }

    private void Assign(string studentId, params (Guid SubjectId, Guid CreditsId)[] subjects)
    {
        var student = new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = studentId, FirstName = studentId, LastName = "S", CollegeId = _college };
        var marks = new MarksMaster { MarksId = Guid.NewGuid(), StudentID = studentId, StdMstId = student.StdMstId, ExamId = _exam, SemesterId = "Sem-6", Pattern = "NEP", CollegeId = _college };
        _context.StudentMasters.Add(student);
        _context.MarksMasters.Add(marks);
        foreach (var (subjectId, creditsId) in subjects)
            foreach (var head in new[] { "H1", "H2" })
                _context.StudentMarks.Add(new StudentMarks { Id = Guid.NewGuid(), MarksId = marks.MarksId, SubjectId = subjectId, CreditsId = creditsId, Head = head });
    }

    private StudentAssignRptDto Request() => new() { CourseId = _course, ExamId = _exam, Pattern = "NEP", Semester = "Sem-6" };

    [Fact]
    public async Task Assign_report_lists_only_the_subjects_each_student_was_assigned()
    {
        var core = Subject("CORE", 2);
        var electiveA = Subject("ELEC-A", 2);
        var electiveB = Subject("ELEC-B", 2);
        Assign("S1", core, electiveA);
        Assign("S2", core, electiveB);
        await _context.SaveChangesAsync();

        var rows = await _service.GetReportAsync(Request());

        Assert.Equal(new[] { "CORE", "ELEC-A" }, rows.Where(r => r.StudentID == "S1").Select(r => r.SubjectCode).OrderBy(c => c));
        Assert.Equal(new[] { "CORE", "ELEC-B" }, rows.Where(r => r.StudentID == "S2").Select(r => r.SubjectCode).OrderBy(c => c));
    }

    [Fact]
    public async Task Credit_report_uses_the_assigned_credit_set_only()
    {
        var core = Subject("CORE", 2);
        // A second credit set for the same subject (another academic year) must not double the rows.
        _context.SubjectCreditMasters.Add(new SubjectCreditMaster { CreditsId = Guid.NewGuid(), SubjectId = core.SubjectId, TotalCredits = "3", AYID = Guid.NewGuid().ToString(), CollegeId = _college });
        var elective = Subject("ELEC-A", 2);
        Assign("S1", core);
        Assign("S2", core, elective);
        await _context.SaveChangesAsync();

        var rows = await _service.GetCreditReportAsync(Request());

        Assert.Equal(2, rows.Count(r => r.StudentID == "S1"));                       // CORE: two heads
        Assert.All(rows.Where(r => r.SubjectCode == "CORE"), r => Assert.Equal("4", r.TotalCredits));
        Assert.Equal(4, rows.Count(r => r.StudentID == "S2"));                       // CORE + ELEC-A, two heads each
    }
}
