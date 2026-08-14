using System;
using System.Collections.Generic;
using Xunit;
using ExamAPI.Models;
using ExamAPI.Services.Result.Engine;

namespace ExamAPI.Tests
{
    public class SubjectPassEvaluatorTests
    {
        /// <summary>Theory /60 pass 24 + Practical /40 pass 16, under the given strategy.</summary>
        private static List<StudentMarks> BuildSubject(int? theory, int? practical, string strategy, int? passPercentage = null, bool theoryAbsent = false, bool practicalAbsent = false)
        {
            var creditMaster = new SubjectCreditMaster
            {
                PassingStrategy = strategy,
                PassPercentage = passPercentage,
                Credits = new List<SubjectCredits>
                {
                    new SubjectCredits { Head = "H1", HeadType = "ESE", HeadOutOf = "60", HeadPass = "24" },
                    new SubjectCredits { Head = "H2", HeadType = "TW", HeadOutOf = "40", HeadPass = "16" }
                }
            };

            return new List<StudentMarks>
            {
                new StudentMarks { Head = "H1", Marks = theory, RawMarks = theory, IsAbsent = theoryAbsent, CreditMaster = creditMaster },
                new StudentMarks { Head = "H2", Marks = practical, RawMarks = practical, IsAbsent = practicalAbsent, CreditMaster = creditMaster }
            };
        }

        [Theory]
        [InlineData(45, 30, true)]  // both heads clear
        [InlineData(40, 15, false)] // practical short -> fails head-wise
        [InlineData(20, 35, false)] // theory short -> fails head-wise
        public void HeadWise_RequiresEveryHeadToClearItsOwnMinimum(int theory, int practical, bool expected)
        {
            var result = SubjectPassEvaluator.Evaluate(BuildSubject(theory, practical, PassingStrategies.HeadWise));

            Assert.False(result.IsCombined);
            Assert.Equal(expected, result.IsPassed);
            Assert.Equal(40, result.RequiredToPass); // 24 + 16
        }

        [Theory]
        [InlineData(45, 30, true)]  // 75 >= 50
        [InlineData(40, 15, true)]  // 55 >= 50: weak practical rescued by a strong theory
        [InlineData(20, 35, true)]  // 55 >= 50: weak theory rescued
        [InlineData(20, 15, false)] // 35 < 50: genuinely fails
        public void Combined_JudgesTheSubjectOnTheSum(int theory, int practical, bool expected)
        {
            var result = SubjectPassEvaluator.Evaluate(BuildSubject(theory, practical, PassingStrategies.Combined, passPercentage: 50));

            Assert.True(result.IsCombined);
            Assert.Equal(50, result.RequiredToPass); // 50% of 100
            Assert.Equal(theory + practical, result.ObtainedTotal);
            Assert.Equal(expected, result.IsPassed);
        }

        [Fact]
        public void Combined_ReportsTheDeficitToBeCondoned()
        {
            var result = SubjectPassEvaluator.Evaluate(BuildSubject(48, 0, PassingStrategies.Combined, passPercentage: 50));

            Assert.False(result.IsPassed);
            Assert.Equal(2, result.Deficit); // 50 required - 48 obtained
        }

        [Fact]
        public void Combined_FallsBackToTheSumOfHeadMinimumsWhenNoThresholdIsConfigured()
        {
            var result = SubjectPassEvaluator.Evaluate(BuildSubject(30, 12, PassingStrategies.Combined));

            Assert.Equal(40, result.RequiredToPass); // 24 + 16
            Assert.True(result.IsPassed);            // 42 >= 40
        }

        [Fact]
        public void AbsentHeadCountsAsZeroButOnlyAllAbsentMarksTheSubjectAbsent()
        {
            var partial = SubjectPassEvaluator.Evaluate(BuildSubject(null, 30, PassingStrategies.Combined, 50, theoryAbsent: true));
            Assert.False(partial.IsAllAbsent);
            Assert.Equal(30, partial.ObtainedTotal);
            Assert.False(partial.IsPassed);

            var whollyAbsent = SubjectPassEvaluator.Evaluate(BuildSubject(null, null, PassingStrategies.Combined, 50, theoryAbsent: true, practicalAbsent: true));
            Assert.True(whollyAbsent.IsAllAbsent);
            Assert.Equal(0, whollyAbsent.ObtainedTotal);
        }

        [Fact]
        public void UnknownStrategyIsTreatedAsHeadWiseSoLegacyRowsCannotSilentlyBecomeCombined()
        {
            var result = SubjectPassEvaluator.Evaluate(BuildSubject(40, 15, strategy: ""));

            Assert.False(result.IsCombined);
            Assert.False(result.IsPassed);
        }
    }
}
