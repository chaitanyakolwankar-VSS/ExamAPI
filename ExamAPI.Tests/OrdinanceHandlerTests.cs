using ExamAPI.DTOs;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Ordinance;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.Result.Engine.ActionHandlers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ExamAPI.Tests
{
    /// <summary>
    /// AddGrace / UpgradeGrade handler behaviour: head-label targeting (BUG-01), the resolution
    /// guard (BUG-05), combined-subject absence / double-grace / upgrade rules (BUG-06/07/08) and
    /// the grace pool / "None" parameter semantics (BUG-09).
    /// </summary>
    public class OrdinanceHandlerTests
    {
        // ------------------------------------------------------------------
        // Fixture helpers
        // ------------------------------------------------------------------

        /// <summary>ESE /60 (pass 24) + IA /40 (pass 16), head-wise or combined at 50%.</summary>
        private static SubjectCreditMaster Credit(bool combined = false, int? passPercentage = null) => new()
        {
            PassingStrategy = combined ? PassingStrategies.Combined : PassingStrategies.HeadWise,
            PassPercentage = passPercentage,
            Credits = new List<SubjectCredits>
            {
                new() { Head = "H1", HeadType = "ESE", HeadOutOf = "60", HeadPass = "24" },
                new() { Head = "H2", HeadType = "IA", HeadOutOf = "40", HeadPass = "16" }
            }
        };

        private static List<StudentMarks> Subject(Guid subjectId, SubjectCreditMaster credit, int? ese, int? ia) => new()
        {
            new StudentMarks { SubjectId = subjectId, Head = "H1", RawMarks = ese, Marks = ese, CreditMaster = credit },
            new StudentMarks { SubjectId = subjectId, Head = "H2", RawMarks = ia, Marks = ia, CreditMaster = credit }
        };

        private static MarksMaster Student(params IEnumerable<StudentMarks>[] subjects)
        {
            var marks = subjects.SelectMany(s => s).ToList();
            return new MarksMaster
            {
                Pattern = "NEP",
                StudentMarks = marks,
                SubjectResults = marks
                    .Select(m => m.SubjectId!.Value).Distinct()
                    .Select(id => new StudentSubjectResult { SubjectId = id })
                    .ToList()
            };
        }

        private static StudentMarks Head(MarksMaster mm, int index) => mm.StudentMarks!.ElementAt(index);

        /// <summary>A live-shaped AddGrace: a fixed per-subject cap, no second limit, pool in MaxLimit.</summary>
        private static RuleAction Grace(string? target, decimal pool = 10, decimal cap = 5, int? maxTargets = null) => new()
        {
            ActionType = "AddGrace",
            CalculationMode = "MinOf",
            Param1Type = "Absolute",
            Param1Value = cap,
            Param2Type = "None",
            MaxLimit = pool,
            MaxTargetCount = maxTargets,
            Target = target
        };

        private static ApplicationDbContext NewContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object);
        }

        private static GradeMaster Grades(params (string grade, decimal min)[] bands) => new()
        {
            Name = "Grades",
            Thresholds = bands.Select(b => new GradeThreshold { Grade = b.grade, MinPercentage = b.min, MaxPercentage = 100 }).ToList()
        };

        private static RuleAction Upgrade(string? target, decimal hardLimit = 10) => new()
        {
            ActionType = "UpgradeGrade",
            Param2Value = hardLimit,
            Target = target
        };

        /// <summary>An UpgradeGrade action already attached to a rule set, as ResultService loads it.</summary>
        private static RuleAction UpgradeWithRuleSet(string? target, GradeMaster grades, decimal hardLimit = 10)
        {
            var action = Upgrade(target, hardLimit);
            action.Rule = new Rule
            {
                Name = "Upgrade",
                RuleSet = new RuleSet { Name = "RS", GradeMaster = grades }
            };
            return action;
        }

        // ------------------------------------------------------------------
        // Task 1 / BUG-01: targets
        // ------------------------------------------------------------------

        [Fact]
        public async Task AddGrace_HeadLabelTarget_GracesOnlyThatHead()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 22, ia: 14)); // both fail by 2

            await new AddGraceHandler().ExecuteAsync(mm, Grace("ESE"), "@");

            Assert.Equal(24, Head(mm, 0).Marks);
            Assert.Equal("2@", Head(mm, 0).Grace);
            Assert.Equal(14, Head(mm, 1).Marks);
            Assert.Null(Head(mm, 1).Grace);
        }

        [Fact]
        public async Task AddGrace_PositionalTarget_StillMatches()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 22, ia: 14));

            await new AddGraceHandler().ExecuteAsync(mm, Grace("H2"), "@");

            Assert.Equal(22, Head(mm, 0).Marks);
            Assert.Equal(16, Head(mm, 1).Marks);
        }

        [Theory]
        [InlineData("FailingHeads")]
        [InlineData("FailingSubjects")]
        [InlineData("All")]
        [InlineData("")]
        [InlineData(null)]
        public async Task AddGrace_LiveKeywordTargets_GraceEveryFailingHead(string? target)
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 22, ia: 14));

            await new AddGraceHandler().ExecuteAsync(mm, Grace(target), "@");

            Assert.Equal(24, Head(mm, 0).Marks);
            Assert.Equal(16, Head(mm, 1).Marks);
        }

        [Fact]
        public async Task AddGrace_PassingSubjectsScope_GracesNothing()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 22, ia: 14));

            await new AddGraceHandler().ExecuteAsync(mm, Grace("PassingSubjects"), "@");

            Assert.Equal(22, Head(mm, 0).Marks);
            Assert.Equal(14, Head(mm, 1).Marks);
        }

        [Fact]
        public async Task AddGrace_LiveShape_PoolAndTargetCountUnchanged()
        {
            // O.5041-style: MinOf(PercentOfAggregate 1, PercentOfSubject 10), MaxLimit 10, one target.
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 22, ia: 13)); // needs 2 and 3
            var action = new RuleAction
            {
                ActionType = "AddGrace", CalculationMode = "MinOf",
                Param1Type = "PercentOfAggregate", Param1Value = 1,
                Param2Type = "PercentOfSubject", Param2Value = 10,
                MaxLimit = 10, MaxTargetCount = 1, Target = "FailingHeads"
            };

            await new AddGraceHandler().ExecuteAsync(mm, action, "@");

            // 1% of 100 = 1 mark: nothing is small enough. Loosen to 5% and only the smaller deficit lands.
            Assert.Equal(22, Head(mm, 0).Marks);
            action.Param1Value = 5;
            await new AddGraceHandler().ExecuteAsync(mm, action, "@");
            Assert.Equal(24, Head(mm, 0).Marks);
            Assert.Equal(13, Head(mm, 1).Marks); // MaxTargetCount 1
        }

        [Fact]
        public async Task AddGrace_PharmacyShape_AbsoluteTwoNoneMaxLimit()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 22, ia: 13)); // needs 2 and 3
            var action = Grace("FailingSubjects", pool: 9999, cap: 2);

            await new AddGraceHandler().ExecuteAsync(mm, action, "$");

            Assert.Equal(24, Head(mm, 0).Marks);
            Assert.Equal(13, Head(mm, 1).Marks);
        }

        // ------------------------------------------------------------------
        // BUG-09: parameter semantics
        // ------------------------------------------------------------------

        [Fact]
        public async Task AddGrace_NoneAsFirstParameter_MeansUnlimitedNotZero()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 22, ia: 14));
            var action = new RuleAction
            {
                ActionType = "AddGrace", CalculationMode = "MinOf",
                Param1Type = "None", Param2Type = "Absolute", Param2Value = 5,
                MaxLimit = 10, Target = "FailingHeads"
            };

            await new AddGraceHandler().ExecuteAsync(mm, action, "@");

            Assert.Equal(24, Head(mm, 0).Marks);
            Assert.Equal(16, Head(mm, 1).Marks);
        }

        [Fact]
        public async Task AddGrace_BlankFirstParameter_StillGrantsNothing()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 22, ia: 14));
            var action = new RuleAction
            {
                ActionType = "AddGrace", CalculationMode = "MinOf",
                Param2Type = "Absolute", Param2Value = 5, MaxLimit = 10, Target = "FailingHeads"
            };

            await new AddGraceHandler().ExecuteAsync(mm, action, "@");

            Assert.Equal(22, Head(mm, 0).Marks);
        }

        [Fact]
        public async Task AddGrace_GraceChartExpressionRule_UnchangedLiveShape()
        {
            // O.5042-A: MinOf(GraceChart 0, None), pool in MaxLimit, chart in the expression.
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 22, ia: 14));
            var action = new RuleAction
            {
                ActionType = "AddGrace", CalculationMode = "MinOf",
                Param1Type = "GraceChart", Param1Value = 0, Param2Type = "None",
                MaxLimit = 7.75m, Target = "FailingHeads",
                Expression = "if(SubjectOutOf<=50,2,if(SubjectOutOf<=100,3,10))"
            };

            await new AddGraceHandler().ExecuteAsync(mm, action, "@");

            Assert.Equal(24, Head(mm, 0).Marks);
            Assert.Equal(16, Head(mm, 1).Marks);
        }

        [Fact]
        public void ValidateActions_RejectsAddGraceWithoutPositivePool()
        {
            var chartNoPool = new RuleActionCreateDto
            {
                ActionType = "AddGrace", Param1Type = "GraceChart", Param1Value = 0, Expression = "3"
            };

            var ex = Assert.Throws<InvalidOperationException>(() => OrdinanceService.ValidateActions(new[] { chartNoPool }));
            Assert.Contains("Max Limit", ex.Message);

            chartNoPool.MaxLimit = 0;
            Assert.Throws<InvalidOperationException>(() => OrdinanceService.ValidateActions(new[] { chartNoPool }));
        }

        [Fact]
        public void ValidateActions_AcceptsConfiguredPoolsAndOtherActions()
        {
            OrdinanceService.ValidateActions(null);
            OrdinanceService.ValidateActions(new[]
            {
                new RuleActionCreateDto { ActionType = "AddGrace", Param1Type = "GraceChart", Param1Value = 0, MaxLimit = 7.75m },
                new RuleActionCreateDto { ActionType = "AddGrace", Param1Type = "Absolute", Param1Value = 5 }, // legacy pool = Param1Value
                new RuleActionCreateDto { ActionType = "AddBonusSGPI" },
                new RuleActionCreateDto { ActionType = "UpgradeGrade", Param1Value = 0 }
            });
        }

        [Fact]
        public async Task CreateAndUpdateRule_RejectInvalidAddGrace_BeforeTouchingTheDatabase()
        {
            var context = NewContext();
            var service = new OrdinanceService(context, new EngineRegistry(Array.Empty<IFactProvider>(), Array.Empty<IActionHandler>()));
            var bad = new List<RuleActionCreateDto>
            {
                new() { ActionType = "AddGrace", Param1Type = "GraceChart", Param1Value = 0 }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRuleAsync(
                new RuleCreateDto { RuleSetId = Guid.NewGuid(), Name = "R", Actions = bad }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateRuleAsync(
                new RuleUpdateDto { RuleId = Guid.NewGuid(), Name = "R", Actions = bad }));

            Assert.Empty(context.Rules);
        }

        // ------------------------------------------------------------------
        // Task 2 / BUG-05: resolution guard
        // ------------------------------------------------------------------

        [Fact]
        public async Task AddGrace_ResolvedHeadIsSkipped_OtherHeadsOfTheSubjectStillEligible()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 20, ia: 14));
            var ese = Head(mm, 0);
            ese.Resolution = 2; ese.Marks = 22; ese.Grace = "^"; // resolved by marks entry, still short

            await new AddGraceHandler().ExecuteAsync(mm, Grace("FailingHeads", pool: 2), "@");

            Assert.Equal(22, ese.Marks);
            Assert.Equal("^", ese.Grace);
            Assert.Equal(16, Head(mm, 1).Marks);
            Assert.Equal("2@", Head(mm, 1).Grace); // the pool was not spent on the resolved head
        }

        [Fact]
        public async Task AddGrace_NeverOverwritesExistingCaretGrace()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 22, ia: 20));
            var ese = Head(mm, 0);
            ese.Grace = "^"; // symbol present although Resolution is 0 / unset

            await new AddGraceHandler().ExecuteAsync(mm, Grace("FailingHeads"), "@");

            Assert.Equal(22, ese.Marks);
            Assert.Equal("^", ese.Grace);
        }

        [Fact]
        public async Task AddGrace_CombinedSubjectWithAResolvedHead_IsSkippedWhole()
        {
            var subjectId = Guid.NewGuid();
            var mm = Student(Subject(subjectId, Credit(combined: true, passPercentage: 50), ese: 30, ia: 12)); // 42/100, deficit 8
            Head(mm, 1).Resolution = 1; Head(mm, 1).Marks = 12; Head(mm, 1).Grace = "^";

            await new AddGraceHandler().ExecuteAsync(mm, Grace("FailingHeads", pool: 10, cap: 10), "@");

            Assert.Equal(0, mm.SubjectResults!.Single().GraceApplied);
        }

        [Fact]
        public async Task UpgradeGrade_AbortsWhenAnyHeadIsResolved()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 50, ia: 30));
            Head(mm, 0).Resolution = 1;
            var action = UpgradeWithRuleSet("All", Grades(("F", 0), ("A", 75)));

            await CreateUpgradeHandler().ExecuteAsync(mm, action, "*");

            Assert.Equal(50, Head(mm, 0).Marks);
            Assert.Equal(30, Head(mm, 1).Marks);
        }

        // ------------------------------------------------------------------
        // BUG-06: absence
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(false, true)]  // partial absence (IA)
        [InlineData(true, false)]  // partial absence (ESE)
        [InlineData(true, true)]   // full absence
        public async Task AddGrace_CombinedSubjectWithAbsence_IsNotGraced(bool eseAbsent, bool iaAbsent)
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(combined: true, passPercentage: 50), ese: 30, ia: 12));
            Head(mm, 1).IsAbsent = iaAbsent;
            Head(mm, 0).IsAbsent = eseAbsent;

            await new AddGraceHandler().ExecuteAsync(mm, Grace("FailingHeads", pool: 10, cap: 10), "@");

            Assert.Equal(0, mm.SubjectResults!.Single().GraceApplied);
        }

        [Fact]
        public async Task AddGrace_CombinedSubjectWithoutAbsence_IsGraced()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(combined: true, passPercentage: 50), ese: 30, ia: 12));

            await new AddGraceHandler().ExecuteAsync(mm, Grace("FailingHeads", pool: 10, cap: 10), "@");

            var result = mm.SubjectResults!.Single();
            Assert.Equal(8, result.GraceApplied); // 50 - 42
            Assert.Equal("@", result.GraceSymbol);
        }

        // ------------------------------------------------------------------
        // BUG-07: second AddGrace on a combined subject
        // ------------------------------------------------------------------

        [Fact]
        public async Task AddGrace_SecondActionDoesNotRetargetAnAlreadyGracedCombinedSubject()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(combined: true, passPercentage: 50), ese: 30, ia: 12));
            var handler = new AddGraceHandler();

            await handler.ExecuteAsync(mm, Grace("FailingHeads", pool: 10, cap: 10), "@");
            await handler.ExecuteAsync(mm, Grace("FailingHeads", pool: 10, cap: 10), "#");

            var result = mm.SubjectResults!.Single();
            Assert.Equal(8, result.GraceApplied);
            Assert.Equal("@", result.GraceSymbol);
        }

        [Fact]
        public async Task AddGrace_SecondActionOnlyCoversTheDeficitStillStanding_AndAccumulates()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(combined: true, passPercentage: 50), ese: 30, ia: 12)); // deficit 8
            var result = mm.SubjectResults!.Single();
            result.GraceApplied = 3; result.GraceSymbol = "@"; // an earlier rule already awarded 3

            await new AddGraceHandler().ExecuteAsync(mm, Grace("FailingHeads", pool: 5, cap: 10), "#");

            Assert.Equal(8, result.GraceApplied); // 3 + the remaining 5, which is exactly what the pool holds
            Assert.Equal("@#", result.GraceSymbol);
        }

        // ------------------------------------------------------------------
        // Task 5 / BUG-08: UpgradeGrade
        // ------------------------------------------------------------------

        private static UpgradeGradeHandler CreateUpgradeHandler() => new(NewContext());

        private static GradeMaster StandardGrades() => Grades(("F", 0), ("P", 40), ("C", 50), ("B", 60), ("A", 75));

        [Fact]
        public async Task UpgradeGrade_HeadLabelTarget_UpgradesOnlyThatHead()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 40, ia: 30)); // ESE 66.7%, IA 75%... IA already at A
            var action = UpgradeWithRuleSet("ESE", StandardGrades());

            await CreateUpgradeHandler().ExecuteAsync(mm, action, "*");

            Assert.Equal(45, Head(mm, 0).Marks); // 60 * 75% = 45
            Assert.Equal("5*", Head(mm, 0).Grace);
            Assert.Equal(30, Head(mm, 1).Marks);
            Assert.Null(Head(mm, 1).Grace);
        }

        [Theory]
        [InlineData("All")]
        [InlineData("PassingSubjects")]
        [InlineData("")]
        public async Task UpgradeGrade_LiveKeywordTargets_UpgradeEveryPassingHead(string target)
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 40, ia: 25)); // ESE 66.7% -> A needs 5; IA 62.5% -> A needs 5
            var action = UpgradeWithRuleSet(target, StandardGrades());

            await CreateUpgradeHandler().ExecuteAsync(mm, action, "*");

            Assert.Equal(45, Head(mm, 0).Marks);
            Assert.Equal(30, Head(mm, 1).Marks);
        }

        [Fact]
        public async Task UpgradeGrade_FailingSubjectsScope_UpgradesNothing()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(), ese: 40, ia: 25));
            var action = UpgradeWithRuleSet("FailingSubjects", StandardGrades());

            await CreateUpgradeHandler().ExecuteAsync(mm, action, "*");

            Assert.Equal(40, Head(mm, 0).Marks);
        }

        [Fact]
        public async Task UpgradeGrade_AbortsWhenACombinedSubjectAlreadyReceivedGrace()
        {
            var headWise = Subject(Guid.NewGuid(), Credit(), ese: 40, ia: 25);
            var combinedId = Guid.NewGuid();
            var combined = Subject(combinedId, Credit(combined: true, passPercentage: 50), ese: 30, ia: 12);
            var mm = Student(headWise, combined);
            mm.SubjectResults!.Single(r => r.SubjectId == combinedId).GraceApplied = 8;

            await CreateUpgradeHandler().ExecuteAsync(mm, UpgradeWithRuleSet("All", StandardGrades()), "*");

            Assert.Equal(40, Head(mm, 0).Marks);
            Assert.Equal(25, Head(mm, 1).Marks);
        }

        [Fact]
        public async Task UpgradeGrade_CombinedSubject_IsJudgedAndUpgradedOnTheSubjectTotal()
        {
            // 40 + 12 = 52/100 passes the 50% combined threshold although IA (12 < 16) fails its own minimum.
            var subjectId = Guid.NewGuid();
            var mm = Student(Subject(subjectId, Credit(combined: true, passPercentage: 50), ese: 40, ia: 12));

            await CreateUpgradeHandler().ExecuteAsync(mm, UpgradeWithRuleSet("All", StandardGrades()), "*");

            var result = mm.SubjectResults!.Single();
            Assert.Equal(8, result.GraceApplied); // 52% -> 60% band
            Assert.Equal("*", result.GraceSymbol);
            Assert.Equal(40, Head(mm, 0).Marks); // heads untouched
            Assert.Equal(12, Head(mm, 1).Marks);
        }

        [Fact]
        public async Task UpgradeGrade_CombinedSubjectThatFails_IsNotUpgraded()
        {
            var mm = Student(Subject(Guid.NewGuid(), Credit(combined: true, passPercentage: 50), ese: 30, ia: 12)); // 42

            await CreateUpgradeHandler().ExecuteAsync(mm, UpgradeWithRuleSet("All", StandardGrades()), "*");

            Assert.Equal(0, mm.SubjectResults!.Single().GraceApplied);
        }

        [Fact]
        public async Task UpgradeGrade_RuleSetLookup_MatchesTheExamType()
        {
            var context = NewContext();
            var pattern = new PatternMaster { PatternId = Guid.NewGuid(), PatternName = "NEP" };
            context.PatternMasters.Add(pattern);

            // Inserted first: a Regular rule set whose next band is 55%.
            context.RuleSets.Add(new RuleSet
            {
                Name = "Regular", ExamType = "Regular", IsActive = true, PatternId = pattern.PatternId,
                GradeMaster = Grades(("F", 0), ("P", 40), ("X", 55))
            });
            // The rule set for this exam: next band 60%.
            context.RuleSets.Add(new RuleSet
            {
                Name = "Atkt", ExamType = "ATKT", IsActive = true, PatternId = pattern.PatternId,
                GradeMaster = Grades(("F", 0), ("P", 40), ("B", 60))
            });
            await context.SaveChangesAsync();

            // 100-mark head, 50 scored. The exam is spelled "A.T.K.T" as Exam Master writes it.
            var credit = new SubjectCreditMaster
            {
                Credits = new List<SubjectCredits> { new() { Head = "H1", HeadType = "ESE", HeadOutOf = "100", HeadPass = "40" } }
            };
            var mm = new MarksMaster
            {
                Pattern = "NEP",
                Exam = new ExamMaster { Name = "KT Exam", ExamType = "A.T.K.T" },
                StudentMarks = new List<StudentMarks>
                {
                    new() { SubjectId = Guid.NewGuid(), Head = "H1", RawMarks = 50, Marks = 50, CreditMaster = credit }
                },
                SubjectResults = new List<StudentSubjectResult>()
            };

            await new UpgradeGradeHandler(context).ExecuteAsync(mm, Upgrade("All"), "*");

            Assert.Equal(60, mm.StudentMarks!.Single().Marks); // ATKT thresholds, not the first active rule set's 55
        }

        [Fact]
        public async Task UpgradeGrade_UnknownExamType_DoesNotGuessARuleSet()
        {
            var context = NewContext();
            var pattern = new PatternMaster { PatternId = Guid.NewGuid(), PatternName = "NEP" };
            context.PatternMasters.Add(pattern);
            context.RuleSets.Add(new RuleSet
            {
                Name = "Regular", ExamType = "Regular", IsActive = true, PatternId = pattern.PatternId,
                GradeMaster = Grades(("F", 0), ("X", 55))
            });
            await context.SaveChangesAsync();

            var credit = new SubjectCreditMaster
            {
                Credits = new List<SubjectCredits> { new() { Head = "H1", HeadType = "ESE", HeadOutOf = "100", HeadPass = "40" } }
            };
            var mm = new MarksMaster
            {
                Pattern = "NEP",
                StudentMarks = new List<StudentMarks>
                {
                    new() { SubjectId = Guid.NewGuid(), Head = "H1", RawMarks = 50, Marks = 50, CreditMaster = credit }
                }
            };

            await new UpgradeGradeHandler(context).ExecuteAsync(mm, Upgrade("All"), "*");

            Assert.Equal(50, mm.StudentMarks!.Single().Marks);
        }
    }
}
