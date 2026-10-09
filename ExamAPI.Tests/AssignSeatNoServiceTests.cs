using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.AssignSeatNo;
using ExamAPI.Services.Common;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>T-52: seat numbers belong to the original exam; the API refuses them on a revaluation exam.</summary>
public sealed class AssignSeatNoServiceTests
{
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly AssignSeatNoService _service;

    public AssignSeatNoServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new AssignSeatNoService(_context, new Mock<IGenericRepository>().Object);
    }

    private (ExamMaster Exam, MarksMaster Marks) ExamWithStudent(Guid? revaluationFor = null)
    {
        var exam = new ExamMaster { ExamId = Guid.NewGuid(), Name = "Nov", ExamType = "Regular", IsActive = true, RevaluationForExamId = revaluationFor, CollegeId = _college };
        var student = new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "S1", FirstName = "A", LastName = "B", CollegeId = _college };
        var marks = new MarksMaster { MarksId = Guid.NewGuid(), StudentID = "S1", StdMstId = student.StdMstId, ExamId = exam.ExamId, SemesterId = "Sem-6", Pattern = "NEP", AcademicYearAYID = _ay, SeatNo = "101", CollegeId = _college };
        _context.Exams.Add(exam);
        _context.StudentMasters.Add(student);
        _context.MarksMasters.Add(marks);
        _context.SaveChanges();
        return (exam, marks);
    }

    private GetAssignSeatNoStudents ListRequest(Guid examId) =>
        new() { ExamId = examId, Ayid = _ay, Pattern = "NEP", Semester = "Sem-6" };

    private static SaveSeatNoRequest Save(Guid marksId) => new()
    {
        Students = [new AssignSeatNoStudents { MarksId = marksId, StudentId = "S1", StudentName = "A B", SeatNo = "999", QuotaType = "" }]
    };

    [Fact]
    public async Task Regular_exam_lists_and_saves_seat_numbers()
    {
        var (exam, marks) = ExamWithStudent();

        Assert.Single(await _service.GetStudents(ListRequest(exam.ExamId)));
        Assert.True((await _service.UpdateSeatNo(Save(marks.MarksId))).Success);
        Assert.Equal("999", (await _context.MarksMasters.SingleAsync(m => m.MarksId == marks.MarksId)).SeatNo);
    }

    [Fact]
    public async Task Revaluation_exam_lists_no_students_and_refuses_a_save()
    {
        var (original, _) = ExamWithStudent();
        var (reval, marks) = ExamWithStudent(revaluationFor: original.ExamId);

        Assert.Empty(await _service.GetStudents(ListRequest(reval.ExamId)));
        var result = await _service.UpdateSeatNo(Save(marks.MarksId));
        Assert.False(result.Success);
        Assert.Equal("101", (await _context.MarksMasters.SingleAsync(m => m.MarksId == marks.MarksId)).SeatNo);
    }
}
