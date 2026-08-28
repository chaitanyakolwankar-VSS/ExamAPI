using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.StatisticalReport;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ExamAPI.Tests;

public sealed class StatisticalReportServiceTests
{
    private readonly ApplicationDbContext _context;
    private readonly StatisticalReportService _service;
    private readonly Guid _collegeId = Guid.NewGuid();
    private readonly Guid _academicYearId = Guid.NewGuid();
    private readonly Guid _courseId = Guid.NewGuid();
    private readonly Guid _examId = Guid.NewGuid();
    private const string Semester = "Sem-6";
    private const string Pattern = "NEP";

    public StatisticalReportServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.CollegeId).Returns(_collegeId);
        _context = new ApplicationDbContext(options, httpContextAccessor.Object, currentUser.Object);
        _service = new StatisticalReportService(_context);
    }

    [Fact]
    public async Task GetReport_uses_processed_subject_and_overall_verdicts_for_all_subject_shapes()
    {
        SeedProcessedExam();

        var response = await _service.GetReportAsync(Request(), _collegeId);

        Assert.True(response.Success);
        var report = Assert.IsType<StatisticalReportDto>(response.Data);
        Assert.Equal(2, report.Rows.Count);
        Assert.Equal(2, report.TotalStudentsAppeared);
        Assert.Equal(1, report.TotalStudentsPassed);
        Assert.Equal(50m, report.OverallPassingPercentage);

        var combined = Assert.Single(report.Rows, row => row.SubjectCode == "COMB");
        Assert.Equal(2, combined.TotalAppeared);
        Assert.Equal(1, combined.TotalPassed);
        Assert.Equal(50m, combined.PassingPercentage);
        Assert.Equal(1, combined.PassedBetween40And60);
        Assert.Equal(0, combined.PassedAtOrAbove60);
        Assert.Equal(2, combined.GraceMarksAwarded);

        var headWise = Assert.Single(report.Rows, row => row.SubjectCode == "HEAD");
        Assert.Equal(2, headWise.TotalAppeared);
        Assert.Equal(1, headWise.TotalPassed);
        Assert.Equal(0, headWise.PassedBetween40And60);
        Assert.Equal(1, headWise.PassedAtOrAbove60);
    }

    [Fact]
    public async Task GetReport_rejects_an_exam_with_unprocessed_students()
    {
        SeedProcessedExam();
        _context.MarksMasters.Add(new MarksMaster
        {
            MarksId = Guid.NewGuid(), ExamId = _examId, AcademicYearAYID = _academicYearId,
            SemesterId = Semester, Pattern = Pattern, CollegeId = _collegeId,
            StdMstId = Guid.NewGuid(), StudentID = "ST003"
        });
        await _context.SaveChangesAsync();

        var response = await _service.GetReportAsync(Request(), _collegeId);

        Assert.False(response.Success);
        Assert.Contains("Results have not been processed", response.Message);
    }

    private StatisticalReportRequestDto Request() => new()
    {
        CourseId = _courseId,
        AcademicYearId = _academicYearId,
        ExamId = _examId,
        SemesterId = Semester,
        Pattern = Pattern
    };

    private void SeedProcessedExam()
    {
        var college = new College
        {
            CollegeId = _collegeId, Name = "Test College", CollegeCode = "TC", CollegeCenter = "Main",
            ContactEmail = "test@example.com", ContactPhone = "0000000000", Address = "Test Address"
        };
        _context.Colleges.Add(college);
        _context.AcademicYears.Add(new AcademicYear
        {
            AYID = _academicYearId, CollegeId = _collegeId, ShortDuration = "2025-26", FullDuration = "2025-2026"
        });
        _context.CourseMasters.Add(new CourseMaster
        {
            CourseId = _courseId, CollegeId = _collegeId, CourseCode = "CS", Name = "Computer Science"
        });
        _context.Exams.Add(new ExamMaster
        {
            ExamId = _examId, CollegeId = _collegeId, CourseId = _courseId, AcademicYearAYID = _academicYearId,
            Name = "Regular May 2026", ExamType = "Regular", IsActive = true
        });

        var combinedSubject = AddSubject("COMB", "Combined Subject", PassingStrategies.Combined, 40);
        var headWiseSubject = AddSubject("HEAD", "Head-wise Subject", PassingStrategies.HeadWise, null);
        var firstStudent = AddStudent("ST001", "Asha", OverallRemarks.Pass);
        var secondStudent = AddStudent("ST002", "Bala", OverallRemarks.Fail);

        AddSubjectResult(firstStudent, combinedSubject, obtained: 48, outOf: 100, passed: true, grace: 2);
        AddSubjectResult(secondStudent, combinedSubject, obtained: 35, outOf: 100, passed: false, grace: 0);
        AddSubjectResult(firstStudent, headWiseSubject, obtained: 70, outOf: 100, passed: true, grace: 0);
        AddSubjectResult(secondStudent, headWiseSubject, obtained: 25, outOf: 100, passed: false, grace: 0);
        _context.SaveChanges();
    }

    private SubjectMaster AddSubject(string code, string name, string passingStrategy, int? passPercentage)
    {
        var subject = new SubjectMaster
        {
            SubjectId = Guid.NewGuid(), SubjectCode = code, Name = name, CourseId = _courseId,
            SemId = Semester, Pattern = Pattern, CollegeId = _collegeId
        };
        _context.SubjectMasters.Add(subject);
        _context.SubjectCreditMasters.Add(new SubjectCreditMaster
        {
            CreditsId = Guid.NewGuid(), SubjectId = subject.SubjectId, CollegeId = _collegeId,
            TotalCredits = "4", PassingStrategy = passingStrategy, PassPercentage = passPercentage
        });
        return subject;
    }

    private MarksMaster AddStudent(string studentId, string firstName, string overallRemark)
    {
        var student = new StudentMaster
        {
            StdMstId = Guid.NewGuid(), StudentId = studentId, FirstName = firstName, LastName = "Student", CollegeId = _collegeId
        };
        _context.StudentMasters.Add(student);
        var marks = new MarksMaster
        {
            MarksId = Guid.NewGuid(), StudentID = studentId, StdMstId = student.StdMstId, ExamId = _examId,
            AcademicYearAYID = _academicYearId, SemesterId = Semester, Pattern = Pattern,
            CollegeId = _collegeId, OverallRemark = overallRemark
        };
        _context.MarksMasters.Add(marks);
        return marks;
    }

    private void AddSubjectResult(MarksMaster marks, SubjectMaster subject, int obtained, int outOf, bool passed, int grace)
    {
        _context.StudentSubjectResults.Add(new StudentSubjectResult
        {
            Id = Guid.NewGuid(), MarksId = marks.MarksId, SubjectId = subject.SubjectId,
            ObtainedTotal = obtained, RawObtainedTotal = obtained - grace, OutOfTotal = outOf,
            GraceApplied = grace, IsPassed = passed,
            SubjectStatus = passed ? SubjectStatuses.Passed : SubjectStatuses.Failed
        });
    }
}
