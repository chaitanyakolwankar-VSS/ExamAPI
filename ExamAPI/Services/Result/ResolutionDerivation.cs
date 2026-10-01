using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ExamAPI.Models;
using ExamAPI.Services.Result.Engine;

namespace ExamAPI.Services.Result
{
    /// <summary>
    /// Derives the staff's resolution ('^') from the ResolutionMaster limits.
    /// <para>
    /// ResolutionMaster is the only source of truth: one row per exam x head, an integer limit
    /// (no upper bound, 0 or blank = off). <c>StudentMarks.Resolution</c>/<c>Grace</c> are a
    /// derived cache that result processing rewrites on every run, so lowering a limit revokes the
    /// bump on the next run and a first-time limit applies on the first run.
    /// </para>
    /// <para>
    /// Head-wise subject: each head that fails its own passing marks by no more than its own limit
    /// is lifted to passing (bump = deficit). Combined subject: a student cannot fail a single
    /// head, so the subject deficit (on the COMBINED marks) is added to the one head that carries
    /// the limit. Both are all-or-nothing -- a partial bump would leave the head/subject failing
    /// while displaying '^'.
    /// </para>
    /// </summary>
    public static class ResolutionDerivation
    {
        public const string Symbol = "^";

        /// <summary>
        /// Parses ResolutionMaster rows into SubjectCreditId -> limit. Zero limits are kept (so a
        /// UI can show an explicit 0); negative or unparseable values count as 0. When the table
        /// holds duplicates for one head the most recently written row wins.
        /// </summary>
        public static Dictionary<Guid, int> ParseLimits(IEnumerable<ResolutionMaster> rows)
        {
            return rows
                .Where(r => !r.IsDeleted)
                .GroupBy(r => r.SubjectCreditID)
                .ToDictionary(g => g.Key, g => ParseLimit(Latest(g).Resolution));
        }

        /// <summary>The row that wins among duplicates for the same exam x head.</summary>
        public static ResolutionMaster Latest(IEnumerable<ResolutionMaster> duplicates) =>
            duplicates.OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt).First();

        /// <summary>Parses a stored limit. Blank, negative or non-integer text means 0 (off).</summary>
        public static int ParseLimit(string? stored) =>
            int.TryParse(stored?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ? limit : 0;

        /// <summary>
        /// Clears the previous run's bump so the head is back to what staff typed. A carried-forward
        /// head (ATKT/Revaluation) is frozen: it keeps the bump it already carries from its source
        /// attempt, because it is not being re-sat and processing must not revoke it.
        /// </summary>
        public static void ResetToRaw(StudentMarks sm)
        {
            if (sm.IsCarryForward)
            {
                var carried = sm.Resolution is > 0 ? sm.Resolution : null;
                sm.Marks = sm.RawMarks is int frozen ? frozen + (carried ?? 0) : null;
                sm.Resolution = carried;
                sm.Grace = carried.HasValue ? Symbol : null;
                return;
            }

            sm.Marks = sm.RawMarks;
            sm.Resolution = null;
            sm.Grace = null;
        }

        /// <summary>
        /// Applies the derivation to one student's heads of ONE subject, which must already be reset
        /// to raw marks. Returns whether a bump was applied.
        /// </summary>
        public static bool Apply(IEnumerable<StudentMarks> subjectHeads, IReadOnlyDictionary<Guid, int> limits)
        {
            if (limits.Count == 0) return false;

            var heads = subjectHeads
                .OrderBy(sm => sm.Head, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (heads.Count == 0) return false;

            var verdict = SubjectPassEvaluator.Evaluate(heads);
            return verdict.IsCombined
                ? ApplyCombined(heads, verdict, limits)
                : ApplyHeadWise(heads, limits);
        }

        private static bool ApplyHeadWise(IList<StudentMarks> heads, IReadOnlyDictionary<Guid, int> limits)
        {
            var applied = false;
            foreach (var sm in heads)
            {
                if (sm.IsCarryForward || sm.IsAbsent || !sm.Marks.HasValue) continue;
                if (LimitFor(sm, limits) is not int limit || limit <= 0) continue;

                var deficit = SubjectPassEvaluator.GetHeadPass(sm) - sm.Marks.Value;
                if (deficit <= 0 || deficit > limit) continue;

                Bump(sm, deficit);
                applied = true;
            }

            return applied;
        }

        private static bool ApplyCombined(IList<StudentMarks> heads, SubjectPassResult verdict, IReadOnlyDictionary<Guid, int> limits)
        {
            // Absent in any head (or nothing typed yet): there is no combined mark to condone.
            if (heads.Any(sm => sm.IsAbsent || !sm.Marks.HasValue)) return false;
            if (verdict.IsPassed || verdict.Deficit <= 0) return false;

            // A carried-forward head that already holds a bump means the subject was resolved in
            // its source attempt; do not stack a second one.
            if (heads.Any(sm => (sm.Resolution ?? 0) > 0)) return false;

            // The limit lives on exactly one head. Legacy data may hold several: the first head
            // by head order wins.
            var target = heads.FirstOrDefault(sm => !sm.IsCarryForward && (LimitFor(sm, limits) ?? 0) > 0);
            if (target == null) return false;

            var limit = LimitFor(target, limits)!.Value;
            if (verdict.Deficit > limit) return false;

            Bump(target, verdict.Deficit);
            return true;
        }

        private static int? LimitFor(StudentMarks sm, IReadOnlyDictionary<Guid, int> limits)
        {
            var credit = SubjectPassEvaluator.FindCredit(sm);
            return credit != null && limits.TryGetValue(credit.Id, out var limit) ? limit : null;
        }

        private static void Bump(StudentMarks sm, int deficit)
        {
            sm.Marks = (sm.RawMarks ?? sm.Marks ?? 0) + deficit;
            sm.Resolution = deficit;
            sm.Grace = Symbol;
            sm.UpdatedAt = DateTime.UtcNow;
        }
    }
}
