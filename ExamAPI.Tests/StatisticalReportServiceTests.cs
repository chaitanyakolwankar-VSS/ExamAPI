using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.StatisticalReport;
using ExamAPI.Services.Result;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.Result.Engine.ActionHandlers;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using OfficeOpenXml;

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

    [Fact]
    public async Task GetReport_merge_uses_the_best_processed_attempt_per_student_and_subject()
    {
        SeedProcessedExam();
        var mergedExamId = Guid.NewGuid();
        var secondStudent = _context.StudentMasters.Single(student => student.StudentId == "ST002");
        var combinedSubject = _context.SubjectMasters.Single(subject => subject.SubjectCode == "COMB");
        var headWiseSubject = _context.SubjectMasters.Single(subject => subject.SubjectCode == "HEAD");
        _context.Exams.Add(new ExamMaster
        {
            ExamId = mergedExamId, CollegeId = _collegeId, CourseId = _courseId,
            AcademicYearAYID = _academicYearId, Name = "ATKT Oct 2026", ExamType = "KT", IsActive = true
        });
        var retry = new MarksMaster
        {
            MarksId = Guid.NewGuid(), StudentID = secondStudent.StudentId, StdMstId = secondStudent.StdMstId,
            ExamId = mergedExamId, AcademicYearAYID = _academicYearId, SemesterId = Semester, Pattern = Pattern,
            CollegeId = _collegeId, OverallRemark = OverallRemarks.Pass
        };
        _context.MarksMasters.Add(retry);
        AddSubjectResult(retry, combinedSubject, obtained: 62, outOf: 100, passed: true, grace: 3);
        AddSubjectResult(retry, headWiseSubject, obtained: 65, outOf: 100, passed: true, grace: 0);
        // The discarded attempt also has grace: it must not be added to the selected retry.
        var discarded = _context.StudentSubjectResults.Single(r => r.SubjectId == combinedSubject.SubjectId
            && r.MarksId == _context.MarksMasters.Single(m => m.StudentID == "ST002" && m.ExamId == _examId).MarksId);
        discarded.RawObtainedTotal = discarded.ObtainedTotal - 4;
        await _context.SaveChangesAsync();

        var response = await _service.GetReportAsync(new StatisticalReportRequestDto
        {
            CourseId = _courseId, AcademicYearId = _academicYearId, ExamId = _examId,
            MergeExam = true, MergedExamId = mergedExamId, SemesterId = Semester, Pattern = Pattern
        }, _collegeId);

        Assert.True(response.Success);
        var report = Assert.IsType<StatisticalReportDto>(response.Data);
        Assert.Equal(2, report.TotalStudentsAppeared);
        Assert.Equal(2, report.TotalStudentsPassed);
        var combined = Assert.Single(report.Rows, row => row.SubjectCode == "COMB");
        Assert.Equal(2, combined.TotalPassed);
        Assert.Equal(1, combined.PassedBetween40And60);
        Assert.Equal(1, combined.PassedAtOrAbove60);
        Assert.Equal(5, combined.GraceMarksAwarded); // ST001: 2, selected ST002 retry: 3 (not 4 + 3).
    }

    private StatisticalReportRequestDto Request() => new()
    {
        CourseId = _courseId,
        AcademicYearId = _academicYearId,
        ExamId = _examId,
        SemesterId = Semester,
        Pattern = Pattern
    };

    // Exercise the real processing pipeline before reading the report, rather than
    // assuming that head-wise grace is stored in SubjectResult.GraceApplied.
    [Theory]
    // Columns: strategy, raw H1, raw H2, resolution LIMIT configured on H1 (ResolutionMaster),
    // resolution actually DERIVED on processing, total (resolution + ordinance) grace, passed.
    [InlineData(PassingStrategies.Combined, 18, 20, 0, 0, 2, true)]
    [InlineData(PassingStrategies.HeadWise, 18, 20, 0, 0, 2, true)]
    // Deficit 3 exceeds the limit of 1: all-or-nothing, so no resolution and ordinance grace covers it.
    [InlineData(PassingStrategies.Combined, 17, 20, 1, 0, 3, true)]
    [InlineData(PassingStrategies.HeadWise, 17, 20, 1, 0, 3, true)]
    // Deficit 1 is within the limit of 1: resolved, so no ordinance grace on top.
    [InlineData(PassingStrategies.Combined, 17, 22, 1, 1, 1, true)]
    [InlineData(PassingStrategies.HeadWise, 19, 20, 1, 1, 1, true)]
    // A larger limit condones the whole deficit of 3 (no upper bound, no headroom cap).
    [InlineData(PassingStrategies.Combined, 17, 20, 9, 3, 3, true)]
    [InlineData(PassingStrategies.HeadWise, 17, 20, 9, 3, 3, true)]
    [InlineData(PassingStrategies.Combined, 30, 30, 0, 0, 0, true)]
    [InlineData(PassingStrategies.HeadWise, 30, 30, 0, 0, 0, true)]
    [InlineData(PassingStrategies.Combined, 10, 20, 0, 0, 0, false)]
    [InlineData(PassingStrategies.HeadWise, 10, 20, 0, 0, 0, false)]
    [InlineData(PassingStrategies.Combined, null, null, 0, 0, 0, false)]
    [InlineData(PassingStrategies.HeadWise, null, null, 0, 0, 0, false)]
    [InlineData(PassingStrategies.HeadWise, 18, 18, 0, 0, 4, true)]
    [InlineData(PassingStrategies.Combined, 18, 18, 0, 0, 4, true)]
    public async Task Processed_grace_and_resolution_match_preview_and_excel_without_compounding(
        string strategy, int? rawH1, int? rawH2, int resolutionLimit, int resolution, int expectedGrace, bool passed)
    {
        SeedProcessedExam();
        var student = _context.MarksMasters.Single(m => m.StudentID == "ST001");
        var subject = _context.SubjectMasters.Single(s => s.SubjectCode == "COMB");
        var credit = _context.SubjectCreditMasters.Single(c => c.SubjectId == subject.SubjectId);
        credit.PassingStrategy = strategy;
        credit.PassPercentage = strategy == PassingStrategies.Combined ? 40 : null;
        AddHeads(student, credit, rawH1, rawH2, resolutionLimit);
        var otherSubject = _context.SubjectMasters.Single(s => s.SubjectCode == "HEAD");
        AddHeads(student, _context.SubjectCreditMasters.Single(c => c.SubjectId == otherSubject.SubjectId), 35, 35, 0);
        SeedGraceRule();
        await _context.SaveChangesAsync();

        var processor = new ResultService(_context,
            new EngineRegistry(Array.Empty<IFactProvider>(), new IActionHandler[] { new AddGraceHandler() }));
        var processRequest = new ProcessResultRequest
        {
            BranchId = _courseId, ExamId = _examId, SemId = Semester, Pattern = Pattern,
            IsSingleStudent = true, StudentId = "ST001"
        };

        // Reprocessing must not accumulate either ordinance grace or resolution.
        for (var run = 0; run < 2; run++)
        {
            var processed = await processor.ProcessResultsAsync(processRequest, _collegeId);
            Assert.True(processed.Success, processed.Message);
            var stored = _context.StudentSubjectResults.Single(r => r.MarksId == student.MarksId && r.SubjectId == subject.SubjectId);
            Assert.Equal((rawH1 ?? 0) + (rawH2 ?? 0), stored.RawObtainedTotal);
            Assert.Equal(stored.RawObtainedTotal + expectedGrace, stored.ObtainedTotal);
            Assert.Equal(passed, stored.IsPassed);
            Assert.Equal(strategy == PassingStrategies.Combined ? expectedGrace - resolution : 0, stored.GraceApplied);

            var response = await _service.GetReportAsync(Request(), _collegeId);
            Assert.True(response.Success, response.Message);
            var report = Assert.IsType<StatisticalReportDto>(response.Data);
            var row = Assert.Single(report.Rows, r => r.SubjectCode == "COMB");
            Assert.Equal(expectedGrace, row.GraceMarksAwarded);
            Assert.Equal(2, row.TotalAppeared);
            Assert.Equal(passed ? 1 : 0, row.TotalPassed);
            Assert.Equal(passed ? 50m : 0m, row.PassingPercentage);
            Assert.Equal(passed && stored.ObtainedTotal < 60 ? 1 : 0, row.PassedBetween40And60);
            Assert.Equal(passed && stored.ObtainedTotal >= 60 ? 1 : 0, row.PassedAtOrAbove60);
            Assert.Equal(passed ? 1 : 0, report.TotalStudentsPassed);

            var export = await _service.GenerateExcelAsync(Request(), _collegeId);
            Assert.True(export.Success, export.Message);
            using var workbook = new ExcelPackage(new MemoryStream(export.Data!));
            var sheet = workbook.Workbook.Worksheets[0];
            Assert.Equal("COMB", sheet.Cells[8, 3].GetValue<string>());
            Assert.Equal(row.TotalAppeared, sheet.Cells[8, 4].GetValue<int>());
            Assert.Equal(row.TotalPassed, sheet.Cells[8, 5].GetValue<int>());
            Assert.Equal(row.PassingPercentage / 100m, sheet.Cells[8, 6].GetValue<decimal>());
            Assert.Equal(row.PassedBetween40And60, sheet.Cells[8, 7].GetValue<int>());
            Assert.Equal(row.PassedAtOrAbove60, sheet.Cells[8, 8].GetValue<int>());
            Assert.Equal(expectedGrace, sheet.Cells[8, 9].GetValue<int>());
            Assert.False(_context.ChangeTracker.HasChanges()); // Reporting never applies grace or edits marks.
        }
    }

    private void AddHeads(MarksMaster student, SubjectCreditMaster credit, int? h1, int? h2, int resolutionLimit)
    {
        foreach (var (head, raw) in new[] { ("H1", h1), ("H2", h2) })
        {
            var headConfig = new SubjectCredits
            {
                Id = Guid.NewGuid(), CreditsId = credit.CreditsId,
                Head = head, HeadType = head == "H1" ? "ESE" : "IA", HeadOutOf = "50", HeadPass = "20"
            };
            _context.Add(headConfig);
            _context.StudentMarks.Add(new StudentMarks
            {
                Id = Guid.NewGuid(), MarksId = student.MarksId, SubjectId = credit.SubjectId,
                CreditsId = credit.CreditsId, Head = head, RawMarks = raw, Marks = raw,
                IsAbsent = raw == null
            });

            // The limit is configuration (ResolutionMaster), not a previously applied mark: the
            // '^' bump is derived when the results are processed.
            if (head == "H1" && resolutionLimit > 0)
            {
                _context.Resolution.Add(new ResolutionMaster
                {
                    ID = Guid.NewGuid(), CollegeId = _collegeId, ExamID = _examId, CreditID = credit.CreditsId,
                    SubjectCreditID = headConfig.Id, Resolution = resolutionLimit,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
    }

    private void SeedGraceRule()
    {
        _context.RuleSets.Add(new RuleSet
        {
            RuleSetId = Guid.NewGuid(), CollegeId = _collegeId, Name = "Regular grace test", ExamType = "Regular", IsActive = true,
            Pattern = new PatternMaster { PatternId = Guid.NewGuid(), PatternName = Pattern, CollegeId = _collegeId },
            GradeMaster = new GradeMaster
            {
                GradeMasterId = Guid.NewGuid(), Name = "Test grades", CollegeId = _collegeId,
                Thresholds = new List<GradeThreshold>
                {
                    new() { ThresholdId = Guid.NewGuid(), Grade = "F", GradePoint = 0, MinPercentage = 0, MaxPercentage = 39.99m },
                    new() { ThresholdId = Guid.NewGuid(), Grade = "P", GradePoint = 4, MinPercentage = 40, MaxPercentage = 100 }
                }
            },
            Rules = new List<Rule>
            {
                new()
                {
                    RuleId = Guid.NewGuid(), Name = "Up to five grace marks", IsEnabled = true, OrdinanceSymbol = "@",
                    Conditions = new List<RuleCondition>(),
                    Actions = new List<RuleAction>
                    {
                        new() { ActionId = Guid.NewGuid(), ActionType = "AddGrace", Target = "All", Param1Type = "Fixed", Param1Value = 5, MaxLimit = 5 }
                    }
                }
            }
        });
    }

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
