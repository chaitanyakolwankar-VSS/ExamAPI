using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ExamAPI.Models;
using ExamAPI.Data;
using ExamAPI.Services.Common;

namespace ExamAPI.Services.Result.Engine.ActionHandlers
{
    public class UpgradeGradeHandler : IActionHandler
    {
        private readonly ApplicationDbContext _context;

        /// <summary>Pool used only when the rule configures no limit at all (no percent, no marks, no MaxLimit).</summary>
        private const decimal DefaultPool = 10;

        public UpgradeGradeHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public string ActionType => "UpgradeGrade";

        /// <summary>
        /// One thing an upgrade can be spent on: a head of a head-wise subject, or a whole
        /// combined subject, where the award lands on the subject result.
        /// </summary>
        private sealed record UpgradeTarget(int Marks, int OutOf, StudentMarks? Head, StudentSubjectResult? SubjectResult);

        public async Task ExecuteAsync(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
            if (marksMaster.StudentMarks == null || !marksMaster.StudentMarks.Any()) return;

            // O.5043 Guideline: Only applies if the student passes ALL subjects WITHOUT the benefit of any other gracing.
            // Combined-subject grace is held on the subject result, not on StudentMarks, so check both.
            if (marksMaster.StudentMarks.Any(sm => !string.IsNullOrEmpty(sm.Grace) || (sm.Resolution ?? 0) > 0)
                || (marksMaster.SubjectResults?.Any(r => r.GraceApplied > 0) ?? false))
            {
                return; // Abort: Student has already used grace/condonation elsewhere
            }

            var upgradeTargets = BuildTargets(marksMaster, action);

            // Calculate total aggregate out of marks
            int aggregateOutOf = marksMaster.StudentMarks.Sum(sm => SubjectPassEvaluator.GetHeadOutOf(sm));

            // Determine total grace available (e.g. 1% of aggregate OR up to 10 marks, whichever is less)
            decimal param1 = action.Param1Value ?? 0; // percentage limit (e.g. 1 for 1%)
            decimal param2 = action.Param2Value ?? 0; // hard limit (e.g. 10)

            decimal limit1 = param1 > 0 ? (aggregateOutOf * param1 / 100) : decimal.MaxValue;
            decimal limit2 = param2 > 0 ? param2 : decimal.MaxValue;
            // MaxLimit is the explicit pool cap the Ordinance UI offers; honour it when set.
            decimal limit3 = action.MaxLimit is > 0 ? action.MaxLimit.Value : decimal.MaxValue;

            decimal totalGraceAvailable = Math.Min(Math.Min(limit1, limit2), limit3);
            if (totalGraceAvailable == decimal.MaxValue) totalGraceAvailable = DefaultPool; // legacy safe default when nothing is configured

            var ruleSet = await ResolveRuleSetAsync(marksMaster, action);

            if (ruleSet?.GradeMaster?.Thresholds == null || !ruleSet.GradeMaster.Thresholds.Any())
                return; // Cannot upgrade without knowing the exact thresholds

            var thresholds = ruleSet.GradeMaster.Thresholds.OrderBy(t => t.MinPercentage).ToList();

            var maxTargetCount = action.MaxTargetCount.GetValueOrDefault();
            var appliedTargetCount = 0;

            foreach (var target in upgradeTargets)
            {
                if (totalGraceAvailable <= 0) break;
                if (maxTargetCount > 0 && appliedTargetCount >= maxTargetCount) break;

                int marks = target.Marks;
                int outOf = target.OutOf;
                if (outOf <= 0) continue;

                decimal currentPercentage = (decimal)marks * 100 / outOf;

                // Find the very next threshold that is strictly greater than their current percentage
                var nextThreshold = thresholds.FirstOrDefault(t => t.MinPercentage > currentPercentage);
                if (nextThreshold != null)
                {
                    // Calculate exact marks required to hit that next threshold
                    int targetMarks = (int)Math.Ceiling(nextThreshold.MinPercentage * outOf / 100);
                    int required = targetMarks - marks;

                    if (required > 0 && required <= totalGraceAvailable)
                    {
                        ApplyUpgrade(target, required, symbol);
                        totalGraceAvailable -= required;
                        appliedTargetCount++;
                    }
                }
            }
        }

        /// <summary>
        /// Naturally passed work only. Head-wise subjects contribute their passing heads; a
        /// combined subject contributes once, judged on the subject verdict rather than its heads.
        /// </summary>
        private static List<UpgradeTarget> BuildTargets(MarksMaster marksMaster, RuleAction action)
        {
            var spec = HeadTargetSpec.Parse(action.Target);
            var targets = new List<UpgradeTarget>();

            // Upgrade only ever lands on passed work, so a scope that excludes passing subjects
            // (e.g. "FailingSubjects") selects nothing.
            if (!spec.MatchesStatus("PASSED")) return targets;

            foreach (var group in marksMaster.StudentMarks!.GroupBy(sm => sm.SubjectId))
            {
                var verdict = SubjectPassEvaluator.Evaluate(group);

                if (verdict.IsCombined)
                {
                    if (spec.RestrictsHeads || !verdict.IsPassed) continue;
                    if (group.Any(sm => sm.IsAbsent || !sm.Marks.HasValue)) continue;

                    var subjectResult = marksMaster.SubjectResults?.FirstOrDefault(r => r.SubjectId == group.Key);
                    if (subjectResult == null) continue;

                    targets.Add(new UpgradeTarget(verdict.ObtainedTotal, verdict.OutOfTotal, null, subjectResult));
                    continue;
                }

                foreach (var sm in group.Where(sm => sm.Marks.HasValue && SubjectPassEvaluator.IsHeadPassed(sm)))
                {
                    if (!spec.MatchesHead(sm)) continue;

                    // A resolved head is never upgraded (the student-level abort already covers
                    // this; kept so the rule reads the same as AddGrace).
                    if ((sm.Resolution ?? 0) > 0) continue;

                    targets.Add(new UpgradeTarget(sm.Marks ?? 0, SubjectPassEvaluator.GetHeadOutOf(sm), sm, null));
                }
            }

            return targets;
        }

        /// <summary>
        /// The rule set the action belongs to, so the thresholds are the ones of the exam being
        /// processed. Falls back to a pattern + exam-type lookup, never to "the first active one".
        /// </summary>
        private async Task<RuleSet?> ResolveRuleSetAsync(MarksMaster marksMaster, RuleAction action)
        {
            var owning = action.Rule?.RuleSet;
            if (owning?.GradeMaster?.Thresholds != null && owning.GradeMaster.Thresholds.Any())
                return owning;

            var examType = marksMaster.Exam?.ExamType;
            if (string.IsNullOrWhiteSpace(examType) && marksMaster.ExamId.HasValue)
            {
                examType = await _context.Exams
                    .Where(e => e.ExamId == marksMaster.ExamId.Value)
                    .Select(e => e.ExamType)
                    .FirstOrDefaultAsync();
            }

            if (string.IsNullOrWhiteSpace(examType)) return null; // cannot pick the right rule set

            var wanted = ExamTypeKeys.Canonical(examType);

            var candidates = await _context.RuleSets
                .Include(rs => rs.GradeMaster)
                .ThenInclude(gm => gm!.Thresholds)
                .Where(rs => rs.Pattern!.PatternName == marksMaster.Pattern && rs.IsActive && !rs.IsDeleted)
                .ToListAsync();

            return candidates.FirstOrDefault(rs => rs.ExamType != null && ExamTypeKeys.Canonical(rs.ExamType) == wanted);
        }

        private static void ApplyUpgrade(UpgradeTarget target, int required, string? symbol)
        {
            if (target.Head is StudentMarks sm)
            {
                sm.Marks = (sm.Marks ?? 0) + required;
                sm.Grace = required.ToString() + (symbol ?? string.Empty);
                return;
            }

            // Combined: the subject total drives the grade, so the award lands on the subject result.
            var result = target.SubjectResult!;
            result.GraceApplied += required;
            result.GraceSymbol = symbol;
        }
    }
}
