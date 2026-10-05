using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Result;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// PLAN-01: resolution ('^') is derived from ResolutionMaster on every result-processing run.
/// These tests seed limits as CONFIG and processing as the only thing that applies them.
/// </summary>
public sealed class ResolutionProcessingTests
{
    private const string Semester = "Sem-6";
    private const string Pattern = "NEP";

    private readonly ApplicationDbContext _context;
    private readonly ResultService _processor;
    private readonly Guid _collegeId = Guid.NewGuid();
    private readonly Guid _courseId = Guid.NewGuid();
    private readonly Guid _examId = Guid.NewGuid();
    private int _studentSeq;

    public ResolutionProcessingTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.CollegeId).Returns(_collegeId);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _processor = new ResultService(_context, new EngineRegistry(Array.Empty<IFactProvider>(), Array.Empty<IActionHandler>()));

        _context.Exams.Add(new ExamMaster
        {
            ExamId = _examId, CollegeId = _collegeId, CourseId = _courseId,
            Name = "Regular", ExamType = "Regular", IsActive = true
        });
        SeedRuleSet(new[] { (0m, 39.99m, "F", 0), (40m, 100m, "P", 4) });
        _context.SaveChanges();
    }

    // ---------------------------------------------------------------- head-wise

    [Fact]
    public async Task HeadWise_bumps_each_head_within_its_own_limit_by_exactly_the_deficit()
    {
        var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 17, 15);
        SetLimit(subject, "H1", 5);
        SetLimit(subject, "H2", 4);
        await _context.SaveChangesAsync();

        await ProcessAsync();

        var h1 = Head(student, "H1");
        Assert.Equal(20, h1.Marks);
        Assert.Equal(17, h1.RawMarks);
        Assert.Equal(3, h1.Resolution);
        Assert.Equal("^", h1.Grace);
    }

    [Fact]
    public async Task HeadWise_is_all_or_nothing_per_head_when_the_deficit_exceeds_the_limit()
    {
        var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 17, 15);
        SetLimit(subject, "H1", 5);
        SetLimit(subject, "H2", 4); // H2 is short by 5: a partial bump of 4 must not happen
        await _context.SaveChangesAsync();

        await ProcessAsync();

        var h2 = Head(student, "H2");
        Assert.Equal(15, h2.Marks);
        Assert.Null(h2.Resolution);
        Assert.Null(h2.Grace);
        Assert.False(_context.StudentSubjectResults.Single(r => r.MarksId == student.MarksId).IsPassed);
    }

    [Fact]
    public async Task HeadWise_skips_an_absent_head_and_a_head_without_a_limit()
    {
        var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, null, 18, absentH1: true);
        SetLimit(subject, "H1", 50);
        await _context.SaveChangesAsync(); // H2 has no limit at all

        await ProcessAsync();

        Assert.Null(Head(student, "H1").Resolution);
        Assert.True(Head(student, "H1").IsAbsent);
        Assert.Null(Head(student, "H2").Resolution);
        Assert.Equal(18, Head(student, "H2").Marks);
    }

    [Fact]
    public async Task A_limit_of_zero_or_blank_or_garbage_switches_resolution_off()
    {
        var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 19, 19);
        SetLimit(subject, "H1", 0);
        _context.Resolution.Add(new ResolutionMaster
        {
            ID = Guid.NewGuid(), CollegeId = _collegeId, ExamID = _examId, SubjectCreditID = Credit(subject, "H2").Id,
            Resolution = -3, CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        await ProcessAsync();

        Assert.All(student.StudentMarks!, sm => Assert.Null(sm.Resolution));
    }

    // ---------------------------------------------------------------- combined

    [Fact]
    public async Task Combined_adds_the_subject_deficit_to_the_selected_head_only()
    {
        var subject = AddSubject(PassingStrategies.Combined, 40, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 20, 17); // 37 of 100, needs 40: deficit 3
        SetLimit(subject, "H2", 5); // the selected head; H1 stays at 0
        SetLimit(subject, "H1", 0);
        await _context.SaveChangesAsync();

        await ProcessAsync();

        var selected = Head(student, "H2");
        Assert.Equal(20, selected.Marks);
        Assert.Equal(3, selected.Resolution);
        Assert.Equal("^", selected.Grace);
        var other = Head(student, "H1");
        Assert.Equal(20, other.Marks);
        Assert.Null(other.Resolution);
        Assert.Null(other.Grace);
        var result = _context.StudentSubjectResults.Single(r => r.MarksId == student.MarksId);
        Assert.True(result.IsPassed);
        Assert.Equal(40, result.ObtainedTotal);
        Assert.Equal(37, result.RawObtainedTotal);
        Assert.Equal(0, result.GraceApplied); // '^' lives on the head, never on the subject grace
    }

    [Fact]
    public async Task Combined_bumps_the_selected_head_although_the_other_head_is_far_below_its_own_passing()
    {
        // A combined subject is judged on the total: 10 + 28 = 38 of 100, deficit 2.
        var subject = AddSubject(PassingStrategies.Combined, 40, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 10, 28);
        SetLimit(subject, "H2", 2);
        await _context.SaveChangesAsync();

        await ProcessAsync();

        Assert.Equal(30, Head(student, "H2").Marks);
        Assert.Equal(2, Head(student, "H2").Resolution);
    }

    [Fact]
    public async Task Combined_is_all_or_nothing_when_the_deficit_exceeds_the_limit()
    {
        var subject = AddSubject(PassingStrategies.Combined, 40, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 20, 14); // deficit 6
        SetLimit(subject, "H1", 5);
        await _context.SaveChangesAsync();

        await ProcessAsync();

        Assert.All(student.StudentMarks!, sm =>
        {
            Assert.Null(sm.Resolution);
            Assert.Null(sm.Grace);
            Assert.Equal(sm.RawMarks, sm.Marks);
        });
        Assert.False(_context.StudentSubjectResults.Single(r => r.MarksId == student.MarksId).IsPassed);
    }

    [Fact]
    public async Task Combined_is_skipped_when_any_head_is_absent()
    {
        var subject = AddSubject(PassingStrategies.Combined, 40, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, null, 39, absentH1: true);
        SetLimit(subject, "H1", 50);
        await _context.SaveChangesAsync();

        await ProcessAsync();

        Assert.All(student.StudentMarks!, sm => Assert.Null(sm.Resolution));
    }

    [Fact]
    public async Task Combined_with_legacy_limits_on_several_heads_uses_the_first_head_by_order()
    {
        var subject = AddSubject(PassingStrategies.Combined, 40, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 20, 18); // deficit 2
        SetLimit(subject, "H2", 9);
        SetLimit(subject, "H1", 4);
        await _context.SaveChangesAsync();

        await ProcessAsync();

        Assert.Equal(2, Head(student, "H1").Resolution);
        Assert.Null(Head(student, "H2").Resolution);
    }

    // ---------------------------------------------------------------- reprocessing, BUG-03, BUG-04

    [Fact]
    public async Task Reprocessing_is_idempotent()
    {
        var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 17, 25);
        SetLimit(subject, "H1", 5);
        await _context.SaveChangesAsync();

        await ProcessAsync();
        var firstRun = (Head(student, "H1").Marks, Head(student, "H1").Resolution, Head(student, "H1").Grace,
            _context.StudentSubjectResults.Single(r => r.MarksId == student.MarksId).ObtainedTotal);
        await ProcessAsync();
        await ProcessAsync();

        Assert.Equal(firstRun, (Head(student, "H1").Marks, Head(student, "H1").Resolution, Head(student, "H1").Grace,
            _context.StudentSubjectResults.Single(r => r.MarksId == student.MarksId).ObtainedTotal));
        Assert.Equal(3, Head(student, "H1").Resolution);
    }

    [Fact]
    public async Task Lowering_a_limit_revokes_the_bump_on_the_next_run_BUG_04()
    {
        var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 17, 25);
        SetLimit(subject, "H1", 5);
        await _context.SaveChangesAsync();
        await ProcessAsync();
        Assert.Equal(3, Head(student, "H1").Resolution);

        SetLimitValue(subject, "H1", 2); // deficit 3 is now over the limit
        await _context.SaveChangesAsync();
        await ProcessAsync();

        Assert.Null(Head(student, "H1").Resolution);
        Assert.Null(Head(student, "H1").Grace);
        Assert.Equal(17, Head(student, "H1").Marks);

        SetLimitValue(subject, "H1", 5);
        await _context.SaveChangesAsync();
        await ProcessAsync();
        Assert.Equal(3, Head(student, "H1").Resolution);

        SetLimitValue(subject, "H1", 0); // removing the limit revokes too
        await _context.SaveChangesAsync();
        await ProcessAsync();
        Assert.Null(Head(student, "H1").Resolution);
        Assert.Equal(17, Head(student, "H1").Marks);
    }

    [Fact]
    public async Task A_first_time_limit_applies_on_the_very_first_processing_BUG_03()
    {
        var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 17, 25);
        await _context.SaveChangesAsync();
        await ProcessAsync();
        Assert.Null(Head(student, "H1").Resolution); // nothing configured yet

        SetLimit(subject, "H1", 3);
        await _context.SaveChangesAsync();
        await ProcessAsync(); // one run is enough -- no second identical save needed

        Assert.Equal(3, Head(student, "H1").Resolution);
        Assert.Equal("^", Head(student, "H1").Grace);
        Assert.True(_context.StudentSubjectResults.Single(r => r.MarksId == student.MarksId).IsPassed);
    }

    [Fact]
    public async Task Duplicate_config_rows_for_one_head_use_the_most_recently_written()
    {
        var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 17, 25);
        var creditId = Credit(subject, "H1").Id;
        _context.Resolution.AddRange(
            new ResolutionMaster { ID = Guid.NewGuid(), CollegeId = _collegeId, ExamID = _examId, SubjectCreditID = creditId, Resolution = 9, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new ResolutionMaster { ID = Guid.NewGuid(), CollegeId = _collegeId, ExamID = _examId, SubjectCreditID = creditId, Resolution = 0, CreatedAt = DateTime.UtcNow.AddDays(-1) });
        await _context.SaveChangesAsync();

        await ProcessAsync();

        Assert.Null(Head(student, "H1").Resolution);
    }

    [Fact]
    public async Task A_carried_forward_head_keeps_the_bump_it_carries_and_is_never_re_derived()
    {
        var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 18, 25);
        var carried = Head(student, "H1");
        carried.IsCarryForward = true;
        carried.Resolution = 2;
        carried.Marks = 20;
        carried.Grace = "^";
        await _context.SaveChangesAsync();

        await ProcessAsync();

        Assert.Equal(20, carried.Marks);
        Assert.Equal(2, carried.Resolution);
        Assert.Equal("^", carried.Grace);
    }

    // ---------------------------------------------------------------- BUG-10, BUG-11

    [Fact]
    public async Task Subject_results_without_any_heads_are_removed_and_stop_counting_BUG_10()
    {
        var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 30, 30);
        var removedSubject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20));
        _context.StudentSubjectResults.Add(new StudentSubjectResult
        {
            Id = Guid.NewGuid(), MarksId = student.MarksId, SubjectId = removedSubject.SubjectId,
            CreditsId = removedSubject.CreditsId, IsPassed = false, SubjectStatus = SubjectStatuses.Failed
        });
        await _context.SaveChangesAsync();

        await ProcessAsync();

        var remaining = _context.StudentSubjectResults.Where(r => r.MarksId == student.MarksId).ToList();
        Assert.Single(remaining);
        Assert.Equal(subject.SubjectId, remaining[0].SubjectId);
        var overall = _context.StudentsOverallResults.Single(r => r.StdMstId == student.StdMstId);
        Assert.Equal("0", overall.KtTheory); // the orphan's failure no longer counts
    }

    [Fact]
    public async Task A_percentage_in_a_gap_between_grade_bands_uses_the_nearest_lower_band_instead_of_throwing_BUG_11()
    {
        // 39.99 -> 40 leaves a hole: 39.995..39.999 matches no band. Reproduce with a wider hole.
        ReplaceGradeBands(new[] { (0m, 30m, "F", 0), (50m, 100m, "A", 8) });
        var subject = AddSubject(PassingStrategies.Combined, 40, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 20, 25); // 45% passes but sits between 30 and 50
        await _context.SaveChangesAsync();

        var processed = await ProcessAsync();

        Assert.True(processed.Success, processed.Message);
        var stored = _context.StudentSubjectResults.Single(r => r.MarksId == student.MarksId);
        Assert.True(stored.IsPassed);
        Assert.Equal("F", stored.Grade); // nearest band below 45%
    }

    [Fact]
    public async Task A_failed_subject_needs_no_grade_table_lookup()
    {
        ReplaceGradeBands(Array.Empty<(decimal, decimal, string, int)>());
        var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
        var student = AddStudent(subject, 5, 5);
        await _context.SaveChangesAsync();

        var processed = await ProcessAsync();

        Assert.True(processed.Success, processed.Message);
        var stored = _context.StudentSubjectResults.Single(r => r.MarksId == student.MarksId);
        Assert.Equal("F", stored.Grade);
        Assert.Equal(0, stored.GradePoint);
    }

    // ---------------------------------------------------------------- helpers

    private Task<ApiResponseDto<object>> ProcessAsync() =>
        _processor.ProcessResultsAsync(new ProcessResultRequest
        {
            BranchId = _courseId, ExamId = _examId, SemId = Semester, Pattern = Pattern
        }, _collegeId);

    private sealed record SubjectSetup(Guid SubjectId, Guid CreditsId, List<SubjectCredits> Heads);

    private SubjectSetup AddSubject(string strategy, int? passPercentage, params (string Head, int OutOf, int Pass)[] heads)
    {
        var subjectId = Guid.NewGuid();
        var creditsId = Guid.NewGuid();
        _context.SubjectMasters.Add(new SubjectMaster
        {
            SubjectId = subjectId, SubjectCode = $"S{subjectId.ToString("N")[..4]}", Name = "Subject", CourseId = _courseId,
            SemId = Semester, Pattern = Pattern, CollegeId = _collegeId
        });
        _context.SubjectCreditMasters.Add(new SubjectCreditMaster
        {
            CreditsId = creditsId, SubjectId = subjectId, CollegeId = _collegeId, TotalCredits = "4",
            PassingStrategy = strategy, PassPercentage = passPercentage
        });
        var configs = heads.Select(h => new SubjectCredits
        {
            Id = Guid.NewGuid(), CreditsId = creditsId, Head = h.Head, HeadType = h.Head == "H1" ? "ESE" : "IA",
            HeadOutOf = h.OutOf.ToString(), HeadPass = h.Pass.ToString()
        }).ToList();
        _context.SubjectCredits.AddRange(configs);
        return new SubjectSetup(subjectId, creditsId, configs);
    }

    private MarksMaster AddStudent(SubjectSetup subject, int? h1, int? h2, bool absentH1 = false)
    {
        var seq = ++_studentSeq;
        var student = new StudentMaster
        {
            StdMstId = Guid.NewGuid(), StudentId = $"ST{seq:000}", FirstName = "S", LastName = seq.ToString(), CollegeId = _collegeId
        };
        _context.StudentMasters.Add(student);
        var marks = new MarksMaster
        {
            MarksId = Guid.NewGuid(), StudentID = student.StudentId, StdMstId = student.StdMstId, ExamId = _examId,
            SemesterId = Semester, Pattern = Pattern, CollegeId = _collegeId
        };
        _context.MarksMasters.Add(marks);

        var raws = new[] { h1, h2 };
        marks.StudentMarks = new List<StudentMarks>();
        for (var i = 0; i < subject.Heads.Count; i++)
        {
            var isAbsent = i == 0 && absentH1;
            var row = new StudentMarks
            {
                Id = Guid.NewGuid(), MarksId = marks.MarksId, SubjectId = subject.SubjectId, CreditsId = subject.CreditsId,
                Head = subject.Heads[i].Head, RawMarks = isAbsent ? null : raws[i], Marks = isAbsent ? null : raws[i], IsAbsent = isAbsent
            };
            marks.StudentMarks.Add(row);
            _context.StudentMarks.Add(row);
        }
        return marks;
    }

    private StudentMarks Head(MarksMaster student, string head) =>
        _context.StudentMarks.Local.Single(sm => sm.MarksId == student.MarksId && sm.Head == head);

    private static SubjectCredits Credit(SubjectSetup subject, string head) => subject.Heads.Single(h => h.Head == head);

    private void SetLimit(SubjectSetup subject, string head, int limit) =>
        _context.Resolution.Add(new ResolutionMaster
        {
            ID = Guid.NewGuid(), CollegeId = _collegeId, ExamID = _examId, CreditID = subject.CreditsId,
            SubjectCreditID = Credit(subject, head).Id, Resolution = limit, CreatedAt = DateTime.UtcNow
        });

    private void SetLimitValue(SubjectSetup subject, string head, int limit) =>
        _context.Resolution.Local.Single(r => r.SubjectCreditID == Credit(subject, head).Id).Resolution = limit;

    private void SeedRuleSet(IEnumerable<(decimal Min, decimal Max, string Grade, int Gp)> bands)
    {
        _context.RuleSets.Add(new RuleSet
        {
            RuleSetId = Guid.NewGuid(), CollegeId = _collegeId, Name = "Regular", ExamType = "Regular", IsActive = true,
            Pattern = new PatternMaster { PatternId = Guid.NewGuid(), PatternName = Pattern, CollegeId = _collegeId },
            GradeMaster = new GradeMaster
            {
                GradeMasterId = Guid.NewGuid(), Name = "Test grades", CollegeId = _collegeId,
                Thresholds = bands.Select(b => new GradeThreshold
                {
                    ThresholdId = Guid.NewGuid(), MinPercentage = b.Min, MaxPercentage = b.Max, Grade = b.Grade, GradePoint = b.Gp
                }).ToList()
            },
            Rules = new List<Rule>()
        });
    }

    private void ReplaceGradeBands(IEnumerable<(decimal Min, decimal Max, string Grade, int Gp)> bands)
    {
        _context.SaveChanges();
        var existing = _context.RuleSets.Include(r => r.GradeMaster).ThenInclude(g => g!.Thresholds).Single();
        _context.GradeThresholds.RemoveRange(existing.GradeMaster!.Thresholds!);
        foreach (var b in bands)
        {
            _context.GradeThresholds.Add(new GradeThreshold
            {
                ThresholdId = Guid.NewGuid(), GradeMasterId = existing.GradeMaster.GradeMasterId,
                MinPercentage = b.Min, MaxPercentage = b.Max, Grade = b.Grade, GradePoint = b.Gp
            });
        }
        _context.SaveChanges();
    }
}
