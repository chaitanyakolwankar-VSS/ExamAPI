using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.ActionHandlers
{
    public class AddGraceHandler : IActionHandler
    {
        public string ActionType => "AddGrace";

        /// <summary>
        /// One thing grace can be spent on: a single failing head (head-wise subject) or a whole
        /// failing subject (combined), where the award lands on the subject result instead.
        /// </summary>
        private sealed record GraceTarget(int Required, int UnitOutOf, StudentMarks? Head, StudentSubjectResult? SubjectResult);

        public Task ExecuteAsync(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
            if (marksMaster.StudentMarks == null) return Task.CompletedTask;

            var targets = BuildGraceTargets(marksMaster, action)
                .OrderBy(target => target.Required)
                .ToList();

            decimal totalGraceAvailable = action.MaxLimit ?? action.Param1Value ?? 0;
            var maxTargetCount = action.MaxTargetCount.GetValueOrDefault();
            var appliedTargetCount = 0;
            var aggregateOutOf = marksMaster.StudentMarks.Sum(SubjectPassEvaluator.GetHeadOutOf);

            foreach (var target in targets)
            {
                if (totalGraceAvailable <= 0) break;
                if (maxTargetCount > 0 && appliedTargetCount >= maxTargetCount) break;

                decimal limit1 = CalculateActionLimit(action.Param1Type, action.Param1Value, aggregateOutOf, target.UnitOutOf, false, action.Expression);
                decimal limit2 = CalculateActionLimit(action.Param2Type, action.Param2Value, aggregateOutOf, target.UnitOutOf, true, action.Expression);

                decimal allowedForThisSubject = CalculateAllowedGrace(action.CalculationMode, limit1, limit2);
                allowedForThisSubject = Math.Min(allowedForThisSubject, totalGraceAvailable);

                if (target.Required <= allowedForThisSubject)
                {
                    ApplyGrace(target, symbol);
                    totalGraceAvailable -= target.Required;
                    appliedTargetCount++;
                }
            }

            return Task.CompletedTask;
        }

        private static IEnumerable<GraceTarget> BuildGraceTargets(MarksMaster marksMaster, RuleAction action)
        {
            var isAllHeads = IsAllFailingHeadsTarget(action.Target);
            var headTargets = isAllHeads
                ? null
                : action.Target.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(t => t.Trim().ToUpperInvariant())
                               .ToList();

            foreach (var group in marksMaster.StudentMarks!.GroupBy(sm => sm.SubjectId))
            {
                var verdict = SubjectPassEvaluator.Evaluate(group);

                if (verdict.IsCombined)
                {
                    // The deficit is a property of the subject, so head-specific targeting has
                    // nothing to select; only an all-heads rule can grace a combined subject.
                    var subjectResult = marksMaster.SubjectResults?.FirstOrDefault(r => r.SubjectId == group.Key);
                    if (isAllHeads && !verdict.IsPassed && subjectResult != null)
                    {
                        yield return new GraceTarget(verdict.Deficit, verdict.OutOfTotal, null, subjectResult);
                    }
                    continue;
                }

                foreach (var sm in group.Where(sm => sm.Marks.HasValue && !SubjectPassEvaluator.IsHeadPassed(sm)))
                {
                    if (headTargets != null && (sm.Head == null || !headTargets.Contains(sm.Head.ToUpperInvariant())))
                    {
                        continue;
                    }

                    var required = SubjectPassEvaluator.GetHeadPass(sm) - (sm.Marks ?? 0);
                    yield return new GraceTarget(required, SubjectPassEvaluator.GetHeadOutOf(sm), sm, null);
                }
            }
        }

        private static void ApplyGrace(GraceTarget target, string? symbol)
        {
            if (target.Head is StudentMarks sm)
            {
                sm.Marks = (sm.RawMarks ?? 0) + (sm.Resolution ?? 0) + target.Required;
                sm.Grace = target.Required.ToString() + (symbol ?? string.Empty);
                return;
            }

            target.SubjectResult!.GraceApplied = target.Required;
            target.SubjectResult.GraceSymbol = symbol;
        }

        /// <summary>
        /// Resolves one of the action's two limits. <paramref name="unitOutOf"/> is the out-of of
        /// whatever is being graced -- the head for a head-wise subject, the whole subject for a
        /// combined one.
        /// </summary>
        private static decimal CalculateActionLimit(string? paramType, decimal? value, int aggregateOutOf, int unitOutOf, bool noneMeansUnlimited, string? expressionStr = null)
        {
            var normalizedType = NormalizeKey(paramType);
            if (string.IsNullOrEmpty(normalizedType) || normalizedType == "NONE")
            {
                return noneMeansUnlimited ? decimal.MaxValue : 0;
            }

            if (!string.IsNullOrEmpty(expressionStr))
            {
                try
                {
                    var ncalcExpr = new NCalc.Expression(expressionStr);
                    ncalcExpr.Parameters["SubjectOutOf"] = (double)unitOutOf;
                    ncalcExpr.Parameters["AggregateOutOf"] = (double)aggregateOutOf;
                    ncalcExpr.Parameters["ParamValue"] = (double)(value ?? 0);

                    var result = ncalcExpr.Evaluate();
                    return Convert.ToDecimal(result);
                }
                catch
                {
                    // Fallback to standard logic if expression fails
                }
            }

            var rawValue = value ?? 0;
            return normalizedType switch
            {
                "PERCENTOFAGGREGATE" => aggregateOutOf * rawValue / 100,
                "PERCENTOFSUBJECT" => unitOutOf * rawValue / 100,
                _ => rawValue
            };
        }

        private static decimal CalculateAllowedGrace(string? calculationMode, decimal limit1, decimal limit2)
        {
            var mode = NormalizeKey(calculationMode);
            return mode switch
            {
                "MINOF" => Math.Min(limit1, limit2),
                "MAXOF" => Math.Max(limit1, limit2),
                "FIXED" => limit1,
                "PERCENTOFSUBJECT" or "PERCENTOFAGGREGATE" => limit1,
                _ => limit1
            };
        }

        private static bool IsAllFailingHeadsTarget(string? target)
        {
            var key = NormalizeKey(target);
            return string.IsNullOrEmpty(key)
                || key is "ALL" or "FAILINGHEADS" or "FAILINGHEAD" or "FAILINGSUBJECTS" or "FAILINGSUBJECT" or "SUBJECT";
        }

        private static string NormalizeKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        }
    }
}
