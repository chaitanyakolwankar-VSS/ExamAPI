using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.AtktRevalExam;
using ExamAPI.Services.Common;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.Result.Engine.FactProviders;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ExamAPI.Tests
{
    /// <summary>
    /// Covers the ATKT / Revaluation assignment migration. The two behaviours worth protecting:
    /// selectability comes from ordinance rule data rather than code, and a head-scoped
    /// re-attempt must carry the unselected heads forward or combined-passing subjects lose
    /// marks they already earned.
    /// </summary>
    public class AtktRevalExamServiceTests
    {
        private readonly ApplicationDbContext _context;
        private readonly AtktRevalExamService _service;
        private Guid? _currentCollegeId;

        // Seeded ids, filled by Seed().
        private readonly Guid _collegeId = Guid.NewGuid();
        private readonly Guid _ayid = Guid.NewGuid();
        private readonly Guid _courseId = Guid.NewGuid();
        private readonly Guid _patternId = Guid.NewGuid();
        private readonly Guid _sourceExamId = Guid.NewGuid();
        private readonly Guid _targetExamId = Guid.NewGuid();
        private readonly Guid _studentId = Guid.NewGuid();

        private Guid _failedSubjectId;    // head-wise, H1 below its minimum
        private Guid _absentSubjectId;    // both heads absent
        private Guid _combinedSubjectId;  // combined passing, clears on the total

        private const string Semester = "Sem-6";
        private const string Pattern = "NEP";

        public AtktRevalExamServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                // The service wraps its writes in a transaction, which the in-memory provider
                // cannot honour. Downgrading the warning lets the write path run here; against
                // SQL Server the transaction is real.
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(u => u.CollegeId).Returns(() => _currentCollegeId);

            _context = new ApplicationDbContext(options, httpContextAccessor.Object, currentUser.Object);
            _currentCollegeId = _collegeId;

            _service = new AtktRevalExamService(
                _context,
                new GenericRepository(_context),
                new EngineRegistry(
                    new IFactProvider[] { new FailedSubjectCountProvider() },
                    Array.Empty<IActionHandler>()));
        }

        // =================================================================
        // Seeding
        // =================================================================

        /// <param name="combinedStrategy">
        /// Whether the third subject uses combined passing. Its marks (30/80 + 18/20 = 48)
        /// clear a 40% combined threshold but fail a head-wise one, so the same data proves
        /// both branches.
        /// </param>
        private void Seed(bool combinedStrategy = true)
        {
            _context.Colleges.Add(new College
            {
                CollegeId = _collegeId, Name = "Test College", CollegeCode = "TC",
                CollegeCenter = "Main", ContactEmail = "test@example.com", ContactPhone = "0000000000"
            });
            _context.AcademicYears.Add(new AcademicYear
            {
                AYID = _ayid, CollegeId = _collegeId,
                ShortDuration = "2024-2025", FullDuration = "2024-2025"
            });
            _context.CourseMasters.Add(new CourseMaster { CourseId = _courseId, Name = "Computer", CourseCode = "CS", CollegeId = _collegeId });
            _context.PatternMasters.Add(new PatternMaster { PatternId = _patternId, PatternName = Pattern, CollegeId = _collegeId });

            _context.Exams.Add(new ExamMaster
            {
                ExamId = _sourceExamId, Name = "May 2025", ExamType = "Regular",
                CourseId = _courseId, AcademicYearAYID = _ayid, IsActive = true, CollegeId = _collegeId
            });
            _context.Exams.Add(new ExamMaster
            {
                ExamId = _targetExamId, Name = "ATKT Oct 2025", ExamType = "KT",
                CourseId = _courseId, AcademicYearAYID = _ayid, IsActive = true, CollegeId = _collegeId
            });

            var student = new StudentMaster
            {
                StdMstId = _studentId, StudentId = "ST001",
                FirstName = "Asha", LastName = "Rao", CollegeId = _collegeId
            };
            _context.StudentMasters.Add(student);
            _context.StudentEligibilities.Add(new StudentEligibility
            {
                Id = Guid.NewGuid(), StdMstId = _studentId, StudentId = "ST001",
                CourseId = _courseId, AYID = _ayid, SemesterId = Semester,
                Pattern = Pattern, CollegeId = _collegeId
            });

            var sourceMarks = new MarksMaster
            {
                MarksId = Guid.NewGuid(), StdMstId = _studentId, StudentID = "ST001",
                ExamId = _sourceExamId, AcademicYearAYID = _ayid, SemesterId = Semester,
                Pattern = Pattern, SeatNo = "A-101", CollegeId = _collegeId,
                CreatedAt = DateTime.UtcNow.AddDays(-30)
            };
            _context.MarksMasters.Add(sourceMarks);

            _failedSubjectId = AddSubject("SUB1", "Failed Subject", PassingStrategies.HeadWise, null,
                sourceMarks, h1Marks: 20, h2Marks: 15, absent: false);

            _absentSubjectId = AddSubject("SUB2", "Absent Subject", PassingStrategies.HeadWise, null,
                sourceMarks, h1Marks: 0, h2Marks: 0, absent: true);

            _combinedSubjectId = AddSubject("SUB3", "Combined Subject",
                combinedStrategy ? PassingStrategies.Combined : PassingStrategies.HeadWise,
                combinedStrategy ? 40 : null,
                sourceMarks, h1Marks: 30, h2Marks: 18, absent: false);

            _context.SaveChanges();
        }

        private Guid AddSubject(
            string code, string name, string strategy, int? passPercentage,
            MarksMaster marks, int h1Marks, int h2Marks, bool absent)
        {
            var subjectId = Guid.NewGuid();
            var creditsId = Guid.NewGuid();

            _context.SubjectMasters.Add(new SubjectMaster
            {
                SubjectId = subjectId, SubjectCode = code, Name = name,
                SemId = Semester, Pattern = Pattern, CourseId = _courseId, CollegeId = _collegeId
            });

            var creditMaster = new SubjectCreditMaster
            {
                CreditsId = creditsId, SubjectId = subjectId, TotalCredits = "4",
                AYID = _ayid.ToString(), PassingStrategy = strategy,
                PassPercentage = passPercentage, CollegeId = _collegeId,
                Credits = new List<SubjectCredits>
                {
                    new() { Id = Guid.NewGuid(), Head = "H1", HeadType = "ESE", HeadOutOf = "80", HeadPass = "32", CreditsId = creditsId },
                    new() { Id = Guid.NewGuid(), Head = "H2", HeadType = "IA",  HeadOutOf = "20", HeadPass = "8",  CreditsId = creditsId }
                }
            };
            _context.SubjectCreditMasters.Add(creditMaster);

            _context.StudentMarks.Add(new StudentMarks
            {
                Id = Guid.NewGuid(), MarksId = marks.MarksId, SubjectId = subjectId,
                CreditsId = creditsId, Head = "H1", Marks = h1Marks, RawMarks = h1Marks, IsAbsent = absent
            });
            _context.StudentMarks.Add(new StudentMarks
            {
                Id = Guid.NewGuid(), MarksId = marks.MarksId, SubjectId = subjectId,
                CreditsId = creditsId, Head = "H2", Marks = h2Marks, RawMarks = h2Marks, IsAbsent = absent
            });

            return subjectId;
        }

        /// <summary>Seeds a KT rule set carrying one AllowExamAssignment action.</summary>
        private void SeedAssignmentRule(string target, int maxTargetCount = 0, params (string Fact, string Op, string Value)[] conditions)
        {
            var ruleSetId = Guid.NewGuid();
            var ruleId = Guid.NewGuid();

            _context.RuleSets.Add(new RuleSet
            {
                RuleSetId = ruleSetId, Name = "ATKT", ExamType = "KT",
                IsActive = true, PatternId = _patternId, CollegeId = _collegeId
            });

            _context.Rules.Add(new Rule
            {
                RuleId = ruleId, Name = "Assign backlog subjects", Priority = 10,
                IsEnabled = true, StopOnSuccess = false, RuleSetId = ruleSetId
            });

            _context.RuleActions.Add(new RuleAction
            {
                ActionId = Guid.NewGuid(), ActionType = "AllowExamAssignment",
                Target = target, MaxTargetCount = maxTargetCount, RuleId = ruleId
            });

            foreach (var (fact, op, value) in conditions)
            {
                _context.RuleConditions.Add(new RuleCondition
                {
                    ConditionId = Guid.NewGuid(), FactName = fact, Operator = op, Value = value, RuleId = ruleId
                });
            }

            _context.SaveChanges();
        }

        private AtktMatrixRequest Request(bool editMode = false) => new()
        {
            CourseId = _courseId,
            Ayid = _ayid,
            Semester = Semester,
            Pattern = Pattern,
            Mode = AssignmentModes.Atkt,
            TargetExamId = _targetExamId,
            EditMode = editMode
        };

        private static AtktCellDto Cell(AtktStudentRowDto row, Guid subjectId) =>
            row.Cells.Single(c => c.SubjectId == subjectId);

        // =================================================================
        // Matrix
        // =================================================================

        [Fact]
        public async Task Matrix_DerivesSubjectStatusFromMarks()
        {
            Seed();

            var result = await _service.GetMatrixAsync(Request());

            Assert.True(result.Success);
            Assert.Equal(3, result.Columns.Count);
            var row = Assert.Single(result.Students);

            Assert.Equal(SubjectStatuses.Failed, Cell(row, _failedSubjectId).Status);
            Assert.Equal(SubjectStatuses.Absent, Cell(row, _absentSubjectId).Status);
            Assert.Equal(SubjectStatuses.Passed, Cell(row, _combinedSubjectId).Status);
            Assert.Equal("A-101", row.SeatNo);
        }

        [Fact]
        public async Task Matrix_CombinedPassing_ClearsOnTheSubjectTotal()
        {
            // 30/80 + 18/20 = 48, which clears 40% of 100 even though H1 is below its own 32.
            Seed(combinedStrategy: true);

            var row = Assert.Single((await _service.GetMatrixAsync(Request())).Students);
            var cell = Cell(row, _combinedSubjectId);

            Assert.Equal(SubjectStatuses.Passed, cell.Status);
            Assert.Equal(48, cell.ObtainedTotal);
            Assert.Equal(40, cell.RequiredToPass);
            Assert.False(cell.Selectable);
            Assert.Equal("Already cleared.", cell.Reason);
        }

        [Fact]
        public async Task Matrix_HeadWisePassing_FailsTheSameMarks()
        {
            // Identical marks, head-wise: H1 30 < 32, so the subject is a backlog.
            Seed(combinedStrategy: false);

            var row = Assert.Single((await _service.GetMatrixAsync(Request())).Students);
            var cell = Cell(row, _combinedSubjectId);

            Assert.Equal(SubjectStatuses.Failed, cell.Status);
            Assert.Equal(40, cell.RequiredToPass);
            Assert.True(cell.Selectable);
        }

        [Fact]
        public async Task Matrix_WithoutRules_UsesBuiltInScope()
        {
            Seed();

            var result = await _service.GetMatrixAsync(Request());
            var row = Assert.Single(result.Students);

            Assert.False(result.Policy!.IsConfigured);
            Assert.True(Cell(row, _failedSubjectId).Selectable);
            Assert.True(Cell(row, _absentSubjectId).Selectable);
            Assert.False(Cell(row, _combinedSubjectId).Selectable);
            Assert.Equal(2, row.BacklogCount);
        }

        [Fact]
        public async Task TargetExams_DoesNotHideKtTargetBecauseOfUnrelatedRegularRuleSet()
        {
            Seed();
            _context.RuleSets.Add(new RuleSet
            {
                RuleSetId = Guid.NewGuid(), Name = "Regular grace", ExamType = "Regular",
                IsActive = true, PatternId = _patternId, CollegeId = _collegeId
            });
            await _context.SaveChangesAsync();

            var targets = await _service.GetTargetExamsAsync(
                _courseId, _ayid, Semester, AssignmentModes.Atkt, null);

            Assert.Contains(targets, exam => exam.ExamId == _targetExamId);
        }

        [Fact]
        public async Task TargetExams_AtktModeOffersOnlyAtktTypeExams_NotRegular()
        {
            Seed();

            // Exam Master authors the ATKT type as "A.T.K.T"; the KT seed covers historical data.
            var dottedAtktId = Guid.NewGuid();
            _context.Exams.Add(new ExamMaster
            {
                ExamId = dottedAtktId, Name = "A.T.K.T Nov 2025", ExamType = "A.T.K.T",
                CourseId = _courseId, AcademicYearAYID = _ayid, IsActive = true, CollegeId = _collegeId
            });
            await _context.SaveChangesAsync();

            var targets = await _service.GetTargetExamsAsync(
                _courseId, _ayid, Semester, AssignmentModes.Atkt, null);

            // Both ATKT-typed exams are offered; the Regular source exam is never an ATKT target.
            Assert.Contains(targets, exam => exam.ExamId == _targetExamId);
            Assert.Contains(targets, exam => exam.ExamId == dottedAtktId);
            Assert.DoesNotContain(targets, exam => exam.ExamId == _sourceExamId);
        }

        [Fact]
        public async Task Matrix_UsesLatestValidAttemptAndSkipsNewerBlankRegistration()
        {
            Seed();
            var sourceMarks = _context.MarksMasters.Single(mm => mm.ExamId == _sourceExamId);
            var blankExamId = Guid.NewGuid();

            _context.Exams.Add(new ExamMaster
            {
                ExamId = blankExamId, Name = "Incomplete later KT", ExamType = "KT",
                CourseId = _courseId, AcademicYearAYID = _ayid, IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(1), CollegeId = _collegeId
            });
            _context.MarksMasters.Add(new MarksMaster
            {
                MarksId = Guid.NewGuid(), StdMstId = _studentId, StudentID = "ST001",
                ExamId = blankExamId, AcademicYearAYID = _ayid, SemesterId = Semester,
                Pattern = Pattern, CreatedAt = DateTime.UtcNow.AddDays(1), CollegeId = _collegeId
            });
            await _context.SaveChangesAsync();

            var row = Assert.Single((await _service.GetMatrixAsync(Request())).Students);

            Assert.Equal(sourceMarks.MarksId, row.SourceMarksId);
            Assert.Equal(_sourceExamId, row.SourceExamId);
            Assert.Equal("Latest valid completed attempt; target exam excluded.", row.SourceSelectionReason);
        }

        [Fact]
        public async Task Matrix_RuleTargetGovernsSelectability()
        {
            // The same passed subject becomes selectable purely by widening the rule's Target,
            // with no code change -- this is the data-driven claim under test.
            Seed();
            SeedAssignmentRule("FailingSubjects,AbsentSubjects,PassingSubjects");

            var result = await _service.GetMatrixAsync(Request());
            var row = Assert.Single(result.Students);

            Assert.True(result.Policy!.IsConfigured);
            Assert.Equal("ATKT", result.Policy.RuleSetName);
            Assert.True(Cell(row, _combinedSubjectId).Selectable);
        }

        [Fact]
        public async Task Matrix_RuleConditionGatesTheStudentOut()
        {
            Seed();
            SeedAssignmentRule("FailingSubjects", conditions: ("FailedSubjectCount", "<=", "0"));

            var result = await _service.GetMatrixAsync(Request());

            Assert.Empty(result.Students);
        }

        [Fact]
        public async Task Matrix_HeadTargetLocksSubjectsWithoutThatHead()
        {
            Seed();
            SeedAssignmentRule("FailingSubjects,AbsentSubjects,VIVA");

            // No subject exposes a VIVA head, so nothing is selectable and the student drops
            // off the candidate list entirely rather than rendering an all-locked row.
            Assert.Empty((await _service.GetMatrixAsync(Request())).Students);
        }

        [Fact]
        public async Task Matrix_EditModeListsOnlyAssignedStudents()
        {
            Seed();

            Assert.Empty((await _service.GetMatrixAsync(Request(editMode: true))).Students);

            await SaveSelection(_failedSubjectId);

            Assert.Single((await _service.GetMatrixAsync(Request(editMode: true))).Students);
            Assert.Empty((await _service.GetMatrixAsync(Request())).Students);
        }

        // =================================================================
        // Save
        // =================================================================

        private Task<ApiResponseDto<AtktSaveResultDto>> SaveSelection(params Guid[] subjectIds) =>
            _service.SaveAsync(new AtktSaveRequest
            {
                Filter = Request(),
                Students = new List<AtktStudentSelectionDto>
                {
                    new() { StdMstId = _studentId, SubjectIds = subjectIds.ToList() }
                }
            });

        private List<StudentMarks> TargetHeads() =>
            _context.StudentMarks
                .Include(sm => sm.MarksMaster)
                .Where(sm => sm.MarksMaster!.ExamId == _targetExamId)
                .ToList();

        [Fact]
        public async Task Save_CreatesAssignmentAndCarriesOtherSubjectsForward()
        {
            Seed();

            var response = await SaveSelection(_failedSubjectId);

            Assert.True(response.Success);
            Assert.Equal(1, response.Data!.StudentsAssigned);

            var target = Assert.Single(_context.MarksMasters.Where(mm => mm.ExamId == _targetExamId));
            Assert.Equal("A-101", target.SeatNo); // seat number carried forward

            var heads = TargetHeads();

            // The re-attempted subject: both heads fresh, nothing carried.
            var reattempted = heads.Where(h => h.SubjectId == _failedSubjectId).ToList();
            Assert.Equal(2, reattempted.Count);
            Assert.All(reattempted, h => Assert.False(h.IsCarryForward));
            Assert.All(reattempted, h => Assert.Null(h.Marks));

            // Everything else carried over with its marks intact.
            var carried = heads.Where(h => h.SubjectId == _combinedSubjectId).ToList();
            Assert.Equal(2, carried.Count);
            Assert.All(carried, h => Assert.True(h.IsCarryForward));
            Assert.Equal(30, carried.Single(h => h.Head == "H1").Marks);
            Assert.Equal(18, carried.Single(h => h.Head == "H2").Marks);
        }

        [Fact]
        public async Task Save_HeadScopedTarget_CarriesUnselectedHeadsForward()
        {
            // Legacy parity: re-sit the theory paper, keep the term work. For a combined
            // subject this is also a correctness requirement -- blanking the carried head
            // would drop the subject total and fail the student on data loss.
            Seed();
            SeedAssignmentRule("FailingSubjects,AbsentSubjects,ESE");

            await SaveSelection(_failedSubjectId);

            var heads = TargetHeads().Where(h => h.SubjectId == _failedSubjectId).ToList();
            Assert.Equal(2, heads.Count);

            var ese = heads.Single(h => h.Head == "H1");
            Assert.False(ese.IsCarryForward);
            Assert.Null(ese.Marks);

            var internalHead = heads.Single(h => h.Head == "H2");
            Assert.True(internalHead.IsCarryForward);
            Assert.Equal(15, internalHead.Marks);
        }

        [Fact]
        public async Task Save_IsIdempotent()
        {
            Seed();

            await SaveSelection(_failedSubjectId);
            await SaveSelection(_failedSubjectId);

            var duplicates = TargetHeads()
                .GroupBy(h => new { h.SubjectId, h.Head })
                .Where(g => g.Count() > 1)
                .ToList();

            Assert.Empty(duplicates);
            Assert.Single(_context.MarksMasters.Where(mm => mm.ExamId == _targetExamId));
        }

        [Fact]
        public async Task Save_IgnoresSubjectsTheRulesDoNotAllow()
        {
            Seed();

            // The combined subject is already cleared, so it is not selectable.
            var response = await SaveSelection(_failedSubjectId, _combinedSubjectId);

            Assert.True(response.Success);
            Assert.Equal(1, response.Data!.SubjectsRegistered);

            var combined = TargetHeads().Where(h => h.SubjectId == _combinedSubjectId).ToList();
            Assert.All(combined, h => Assert.True(h.IsCarryForward));
        }

        [Fact]
        public async Task Save_RespectsMaxTargetCount()
        {
            Seed();
            SeedAssignmentRule("FailingSubjects,AbsentSubjects", maxTargetCount: 1);

            var response = await SaveSelection(_failedSubjectId, _absentSubjectId);

            Assert.Empty(_context.MarksMasters.Where(mm => mm.ExamId == _targetExamId));
            Assert.Single(response.Data!.Skipped);
        }

        [Fact]
        public async Task Save_EmptySelectionUnassigns()
        {
            Seed();
            await SaveSelection(_failedSubjectId);

            var response = await SaveSelection();

            Assert.Equal(1, response.Data!.StudentsRemoved);
            Assert.Empty(_context.MarksMasters.Where(mm => mm.ExamId == _targetExamId));
            Assert.Empty(TargetHeads());
        }

        [Fact]
        public async Task AssignAll_AssignsEveryEligibleSubject()
        {
            Seed();

            var response = await _service.AssignAllAsync(new AtktAssignAllRequest { Filter = Request() });

            Assert.True(response.Success);
            Assert.Equal(1, response.Data!.StudentsAssigned);

            var reattempted = TargetHeads().Where(h => !h.IsCarryForward).Select(h => h.SubjectId).Distinct().ToList();
            Assert.Equal(2, reattempted.Count); // failed + absent, not the cleared one
        }

        // =================================================================
        // Delete
        // =================================================================

        [Fact]
        public async Task Delete_RemovesAssignmentWhenNoMarksEntered()
        {
            Seed();
            await SaveSelection(_failedSubjectId);

            var response = await _service.DeleteAssignmentAsync(new AtktDeleteRequest
            {
                Filter = Request(),
                StdMstId = _studentId
            });

            Assert.True(response.Success);
            Assert.Empty(_context.MarksMasters.Where(mm => mm.ExamId == _targetExamId));
        }

        [Fact]
        public async Task Delete_IsRefusedOnceMarksAreEntered()
        {
            Seed();
            await SaveSelection(_failedSubjectId);

            var head = TargetHeads().First(h => !h.IsCarryForward);
            head.Marks = 55;
            await _context.SaveChangesAsync();

            var response = await _service.DeleteAssignmentAsync(new AtktDeleteRequest
            {
                Filter = Request(),
                StdMstId = _studentId
            });

            Assert.False(response.Success);
            Assert.Contains("marks have already been entered", response.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(_context.MarksMasters.Where(mm => mm.ExamId == _targetExamId));
        }

        [Fact]
        public async Task Matrix_FlagsAssignmentsThatCanNoLongerBeDeleted()
        {
            Seed();
            await SaveSelection(_failedSubjectId);

            var head = TargetHeads().First(h => !h.IsCarryForward);
            head.Marks = 55;
            await _context.SaveChangesAsync();

            var row = Assert.Single((await _service.GetMatrixAsync(Request(editMode: true))).Students);

            Assert.False(row.CanDelete);
            Assert.NotNull(row.DeleteBlockedReason);
        }
    }

    /// <summary>
    /// The Target vocabulary shared by ordinance actions and exam assignment. Head tokens must
    /// resolve against the printed label (HeadType) as well as the positional key, because the
    /// Ordinance UI tells rule authors to type the configured head name.
    /// </summary>
    public class HeadTargetSpecTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("All")]
        [InlineData("AllSubjects")]
        public void EmptyOrAll_MatchesEveryStatusAndHead(string? target)
        {
            var spec = HeadTargetSpec.Parse(target);

            Assert.True(spec.MatchesStatus(SubjectStatuses.Passed));
            Assert.True(spec.MatchesStatus(SubjectStatuses.Failed));
            Assert.True(spec.MatchesHead("H1", "ESE"));
            Assert.False(spec.RestrictsHeads);
        }

        [Fact]
        public void SubjectScopeKeywords_NarrowStatuses()
        {
            var spec = HeadTargetSpec.Parse("FailingSubjects,AbsentSubjects");

            Assert.True(spec.MatchesStatus(SubjectStatuses.Failed));
            Assert.True(spec.MatchesStatus(SubjectStatuses.Absent));
            Assert.False(spec.MatchesStatus(SubjectStatuses.Passed));
        }

        [Fact]
        public void HeadTokens_MatchLabelAndPositionalKey()
        {
            var spec = HeadTargetSpec.Parse("FailingSubjects,ESE");

            Assert.True(spec.RestrictsHeads);
            Assert.True(spec.MatchesHead("H1", "ESE"));   // by printed label
            Assert.True(spec.MatchesHead("ESE", null));   // by positional key
            Assert.False(spec.MatchesHead("H2", "IA"));
        }

        [Fact]
        public void HeadTokens_IgnorePunctuationAndCase()
        {
            var spec = HeadTargetSpec.Parse("ese (th)");

            Assert.True(spec.MatchesHead("H1", "ESE(TH)"));
            Assert.False(spec.MatchesHead("H1", "ESE"));
        }

        [Fact]
        public void AllSubjects_DoesNotDiscardHeadTokensAfterIt()
        {
            var spec = HeadTargetSpec.Parse("AllSubjects,ESE");

            Assert.True(spec.MatchesStatus(SubjectStatuses.Passed));
            Assert.True(spec.RestrictsHeads);
            Assert.True(spec.MatchesHead("H1", "ESE"));
            Assert.False(spec.MatchesHead("H2", "IA"));
        }

        [Fact]
        public void Union_TakesTheMorePermissiveSide()
        {
            var narrow = HeadTargetSpec.Parse("FailingSubjects,ESE");
            var wide = HeadTargetSpec.Parse("AllSubjects");

            var union = narrow.Union(wide);

            Assert.True(union.MatchesStatus(SubjectStatuses.Passed));
            Assert.False(union.RestrictsHeads);
        }
    }

    /// <summary>
    /// The operator vocabulary is shared with result processing, so both the symbol and the
    /// word spelling of every operator must keep working.
    /// </summary>
    public class RuleConditionEvaluatorTests
    {
        [Theory]
        [InlineData(5, "==", "5", true)]
        [InlineData(5, "Equals", "5", true)]
        [InlineData(5, "!=", "4", true)]
        [InlineData(5, "NotEquals", "5", false)]
        [InlineData(5, ">", "4", true)]
        [InlineData(5, "GreaterThan", "5", false)]
        [InlineData(5, "<", "6", true)]
        [InlineData(5, "LessThan", "5", false)]
        [InlineData(5, ">=", "5", true)]
        [InlineData(5, "GreaterOrEqual", "5", true)]
        [InlineData(5, "GreaterThanOrEqual", "6", false)]
        [InlineData(5, "<=", "5", true)]
        [InlineData(5, "LessOrEqual", "4", false)]
        [InlineData(5, "LessThanOrEqual", "5", true)]
        public void Compare_HandlesBothSpellings(double fact, string op, string value, bool expected)
            => Assert.Equal(expected, RuleConditionEvaluator.Compare(fact, op, value));

        [Theory]
        [InlineData("not-a-number")]
        [InlineData("")]
        [InlineData(null)]
        public void Compare_ReturnsFalseForUnparseableValue(string? value)
            => Assert.False(RuleConditionEvaluator.Compare(5, "==", value));

        [Fact]
        public void Compare_ReturnsFalseForUnknownOperator()
            => Assert.False(RuleConditionEvaluator.Compare(5, "~=", "5"));
    }
}
