using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ExamAPI.Models;
using ExamAPI.Data;

namespace ExamAPI.Services.Result.Engine.ActionHandlers
{
    public class UpgradeGradeHandler : IActionHandler
    {
        private readonly ApplicationDbContext _context;

        public UpgradeGradeHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public string ActionType => "UpgradeGrade";

        public async Task ExecuteAsync(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
            if (marksMaster.StudentMarks == null || !marksMaster.StudentMarks.Any()) return;

            // O.5043 Guideline: Only applies if the student passes ALL subjects WITHOUT the benefit of any other gracing.
            if (marksMaster.StudentMarks.Any(sm => !string.IsNullOrEmpty(sm.Grace) || (sm.Resolution ?? 0) > 0))
            {
                return; // Abort: Student has already used grace/condonation elsewhere
            }

            // Target ONLY subjects that have naturally passed.
            var passingSubjectsQuery = marksMaster.StudentMarks
                .Where(sm => sm.Marks.HasValue && SubjectPassEvaluator.IsHeadPassed(sm));

            // Apply Action Target Filter
            if (!IsAllSubjectsTarget(action.Target))
            {
                var targets = action.Target.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Select(t => t.Trim().ToUpperInvariant())
                                           .ToList();

                passingSubjectsQuery = passingSubjectsQuery.Where(sm => sm.Head != null && targets.Contains(sm.Head.ToUpperInvariant()));
            }

            var passingSubjects = passingSubjectsQuery.ToList();

            // Calculate total aggregate out of marks
            int aggregateOutOf = marksMaster.StudentMarks.Sum(sm => SubjectPassEvaluator.GetHeadOutOf(sm));

            // Determine total grace available (e.g. 1% of aggregate OR up to 10 marks, whichever is less)
            decimal param1 = action.Param1Value ?? 0; // percentage limit (e.g. 1 for 1%)
            decimal param2 = action.Param2Value ?? 0; // hard limit (e.g. 10)
            
            decimal limit1 = param1 > 0 ? (aggregateOutOf * param1 / 100) : decimal.MaxValue;
            decimal limit2 = param2 > 0 ? param2 : decimal.MaxValue;
            
            decimal totalGraceAvailable = Math.Min(limit1, limit2);
            if (totalGraceAvailable == decimal.MaxValue) totalGraceAvailable = 10; // safe default

            // Fetch the GradeMaster based on the student's pattern to get the thresholds
            var ruleSet = await _context.RuleSets
                .Include(rs => rs.GradeMaster)
                .ThenInclude(gm => gm!.Thresholds)
                .Where(rs => rs.Pattern!.PatternName == marksMaster.Pattern && rs.IsActive && !rs.IsDeleted)
                .FirstOrDefaultAsync();

            if (ruleSet?.GradeMaster?.Thresholds == null || !ruleSet.GradeMaster.Thresholds.Any())
                return; // Cannot upgrade without knowing the exact thresholds

            var thresholds = ruleSet.GradeMaster.Thresholds.OrderBy(t => t.MinPercentage).ToList();

            var maxTargetCount = action.MaxTargetCount.GetValueOrDefault();
            var appliedTargetCount = 0;

            foreach (var sm in passingSubjects)
            {
                if (totalGraceAvailable <= 0) break;
                if (maxTargetCount > 0 && appliedTargetCount >= maxTargetCount) break;

                int marks = sm.Marks ?? 0;
                int outOf = SubjectPassEvaluator.GetHeadOutOf(sm);
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
                        ApplyGraceToMark(sm, required, symbol, "Upgraded by Ordinance");
                        totalGraceAvailable -= required;
                        appliedTargetCount++;
                    }
                }
            }
        }

        private static void ApplyGraceToMark(StudentMarks sm, int required, string? symbol, string remark)
        {
            sm.Marks = (sm.Marks ?? 0) + required;
            sm.Grace = required.ToString() + (symbol ?? string.Empty);
        }

        private static bool IsAllSubjectsTarget(string? target)
        {
            var key = string.IsNullOrWhiteSpace(target)
                ? string.Empty
                : new string(target.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

            return string.IsNullOrEmpty(key)
                || key is "ALL" or "PASSINGSUBJECTS" or "PASSINGSUBJECT" or "SUBJECT";
        }
    }
}
