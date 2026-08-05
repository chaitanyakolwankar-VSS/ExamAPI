using System;
using System.Collections.Generic;
using System.Linq;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine
{
    /// <summary>The verdict for one subject, derived from its heads and its credit config.</summary>
    public sealed record SubjectPassResult(
        bool IsCombined,
        int ObtainedTotal,
        int RawObtainedTotal,
        int OutOfTotal,
        int RequiredToPass,
        int Deficit,
        bool IsPassed,
        bool IsAllAbsent);

    /// <summary>
    /// The single place that decides whether a subject is passed. Grade calculation, the
    /// FailedHeadCount/FailedSubjectCount facts, ordinance grace, marks entry and the gazette
    /// all route through here -- nothing else may compare Marks against HeadPass.
    /// <para>
    /// A head's out-of and passing marks are stored as strings on SubjectCredits; anything
    /// unparseable resolves to 0 here, which keeps reports and marks entry rendering instead
    /// of throwing on half-configured subjects.
    /// </para>
    /// </summary>
    public static class SubjectPassEvaluator
    {
        /// <summary>Evaluates one subject from all of its head rows.</summary>
        public static SubjectPassResult Evaluate(IEnumerable<StudentMarks> headGroup)
        {
            var heads = headGroup as IList<StudentMarks> ?? headGroup.ToList();

            var outOfTotal = heads.Sum(GetHeadOutOf);
            var obtainedTotal = heads.Sum(sm => sm.Marks ?? 0);
            var rawObtainedTotal = heads.Sum(sm => sm.RawMarks ?? 0);
            var isAllAbsent = heads.Count > 0 && heads.All(sm => sm.IsAbsent);

            var creditMaster = heads.Select(sm => sm.CreditMaster).FirstOrDefault(c => c != null);
            var isCombined = IsCombined(creditMaster);

            int requiredToPass;
            int deficit;
            bool isPassed;

            if (isCombined)
            {
                requiredToPass = GetCombinedRequirement(creditMaster, outOfTotal, heads);
                isPassed = obtainedTotal >= requiredToPass;
                deficit = Math.Max(0, requiredToPass - obtainedTotal);
            }
            else
            {
                requiredToPass = heads.Sum(GetHeadPass);
                isPassed = heads.All(IsHeadPassed);
                deficit = heads.Sum(sm => Math.Max(0, GetHeadPass(sm) - (sm.Marks ?? 0)));
            }

            return new SubjectPassResult(
                isCombined,
                obtainedTotal,
                rawObtainedTotal,
                outOfTotal,
                requiredToPass,
                deficit,
                isPassed,
                isAllAbsent);
        }

        /// <summary>Whether this subject is configured for combined passing.</summary>
        public static bool IsCombined(SubjectCreditMaster? creditMaster) =>
            string.Equals(creditMaster?.PassingStrategy, PassingStrategies.Combined, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Per-head pass. Meaningful for display and for head-wise subjects; for a combined
        /// subject the subject verdict from <see cref="Evaluate"/> is what counts.
        /// </summary>
        public static bool IsHeadPassed(StudentMarks sm) => (sm.Marks ?? 0) >= GetHeadPass(sm);

        /// <summary>Passing marks configured for this head, 0 when unset or unparseable.</summary>
        public static int GetHeadPass(StudentMarks sm) => ParseHeadValue(FindCredit(sm)?.HeadPass);

        /// <summary>Maximum marks configured for this head, 0 when unset or unparseable.</summary>
        public static int GetHeadOutOf(StudentMarks sm) => ParseHeadValue(FindCredit(sm)?.HeadOutOf);

        /// <summary>The configured head row backing this mark, matched on the positional head key.</summary>
        public static SubjectCredits? FindCredit(StudentMarks sm) =>
            sm.CreditMaster?.Credits?.FirstOrDefault(c => string.Equals(c.Head, sm.Head, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// What to show a user for this head. Head itself is a positional key ("H1"), so the
        /// semantic HeadType ("ESE", "IA") is what reaches gazettes, marksheets and the entry grid.
        /// </summary>
        public static string GetHeadLabel(StudentMarks sm)
        {
            var headType = FindCredit(sm)?.HeadType;
            return string.IsNullOrWhiteSpace(headType) ? sm.Head ?? string.Empty : headType;
        }

        private static int GetCombinedRequirement(SubjectCreditMaster? creditMaster, int outOfTotal, IList<StudentMarks> heads)
        {
            var passPercentage = creditMaster?.PassPercentage;
            if (passPercentage is null or <= 0)
            {
                // No threshold configured -- fall back to the sum of the per-head minimums.
                return heads.Sum(GetHeadPass);
            }

            // Obtained marks are integers, so rounding the threshold up is equivalent to
            // comparing against the exact percentage, and gives a whole-number deficit.
            return (int)Math.Ceiling(outOfTotal * passPercentage.Value / 100.0);
        }

        private static int ParseHeadValue(string? value) => int.TryParse(value, out var parsed) ? parsed : 0;
    }
}
