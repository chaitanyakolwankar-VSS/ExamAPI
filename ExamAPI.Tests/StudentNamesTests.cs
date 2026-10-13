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

/// <summary>A student's full name is shown everywhere as LAST FIRST MIDDLE MOTHER.</summary>
public sealed class StudentNamesTests
{
    [Theory]
    [InlineData("BADAL", "SONIYA", "JORA", "SEEMA", "BADAL SONIYA JORA SEEMA")]
    [InlineData("Patil", "Asha", null, "Sunita", "Patil Asha Sunita")]
    [InlineData("Patil", "Asha", "R", null, "Patil Asha R")]
    [InlineData("Patil", "Asha", "", "  ", "Patil Asha")]
    [InlineData("  Patil ", " Asha", "R  ", " Sunita ", "Patil Asha R Sunita")]
    [InlineData("Patil", "Asha  Rani", null, null, "Patil Asha Rani")]
    [InlineData(null, "Asha", null, "Sunita", "Asha Sunita")]
    [InlineData(null, null, null, null, "")]
    public void Display_orders_last_first_middle_mother_and_skips_blank_parts(string? last, string? first, string? middle, string? mother, string expected) =>
        Assert.Equal(expected, StudentNames.Display(last, first, middle, mother));

    [Fact]
    public void Display_keeps_the_stored_capitalisation()
    {
        var student = new StudentMaster { StudentId = "S1", FirstName = "soniya", MiddleName = "Jora", LastName = "BADAL", MotherName = "seema" };

        Assert.Equal("BADAL soniya Jora seema", StudentNames.Display(student));
    }

    [Fact]
    public async Task Seat_number_list_shows_the_name_in_last_first_middle_mother_order()
    {
        var college = Guid.NewGuid();
        var ay = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(college);
        using var context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        var exam = new ExamMaster { ExamId = Guid.NewGuid(), Name = "Nov", ExamType = "Regular", IsActive = true, CollegeId = college };
        var student = new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "S1", FirstName = "SONIYA", MiddleName = "JORA", LastName = "BADAL", MotherName = "SEEMA", CollegeId = college };
        context.Exams.Add(exam);
        context.StudentMasters.Add(student);
        context.MarksMasters.Add(new MarksMaster { MarksId = Guid.NewGuid(), StudentID = "S1", StdMstId = student.StdMstId, ExamId = exam.ExamId, SemesterId = "Sem-6", Pattern = "NEP", AcademicYearAYID = ay, CollegeId = college });
        context.SaveChanges();
        var service = new AssignSeatNoService(context, new Mock<IGenericRepository>().Object);

        var students = await service.GetStudents(new GetAssignSeatNoStudents { ExamId = exam.ExamId, Ayid = ay, Pattern = "NEP", Semester = "Sem-6" });

        Assert.Equal("BADAL SONIYA JORA SEEMA", Assert.Single(students).StudentName);
    }
}
