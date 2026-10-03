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

        /// <summary>Resolution symbol written by marks entry (see ResultService.RESOLUTION_SYMBOL).</summary>
        private const string ResolutionSymbol = "^";

        /// <summary>StudentSubjectResult.GraceSymbol is [MaxLength(10)].</summary>
        private const int GraceSymbolMaxLength = 10;

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

            // The pool comes from MaxLimit; Param1Value is the legacy fallback. OrdinanceService
            // refuses to save an AddGrace rule whose pool would be zero.
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
                    // The pool and the target count are spent only when the grace actually landed.
                    if (!ApplyGrace(target, symbol)) continue;
                    totalGraceAvailable -= target.Required;
                    appliedTargetCount++;
                }
            }

            return Task.CompletedTask;
        }

        private static IEnumerable<GraceTarget> BuildGraceTargets(MarksMaster marksMaster, RuleAction action)
        {
            // The same parser assignment uses, so a head-named target ("ESE") matches the printed
            // HeadType label as well as the positional key. Keyword targets keep their meaning:
            // FailingHeads / FailingSubjects / All / empty select every failing head.
            var spec = HeadTargetSpec.Parse(action.Target);

            // Grace only ever lands on failing work, so a scope that excludes failing subjects
            // (e.g. "PassingSubjects") selects nothing.
            if (!spec.MatchesStatus("FAILED")) yield break;

            foreach (var group in marksMaster.StudentMarks!.GroupBy(sm => sm.SubjectId))
            {
                var verdict = SubjectPassEvaluator.Evaluate(group);

                if (verdict.IsCombined)
                {
                    // The deficit is a property of the subject, so head-specific targeting has
                    // nothing to select; only an all-heads rule can grace a combined subject.
                    if (spec.RestrictsHeads || verdict.IsPassed) continue;

                    var subjectResult = marksMaster.SubjectResults?.FirstOrDefault(r => r.SubjectId == group.Key);
                    if (subjectResult == null) continue;

                    // Absence (all or partial) is the result for that subject; grace must not pass it.
                    if (group.Any(sm => sm.IsAbsent)) continue;

                    // Resolution (^) is the remedy for the heads it touched: one resolved head
                    // takes the whole combined subject out of ordinance grace.
                    if (group.Any(IsResolved)) continue;

                    // The verdict reads StudentMarks only, so grace an earlier action already
                    // awarded is not in it. Only the deficit still standing is up for grace.
                    var remaining = verdict.Deficit - subjectResult.GraceApplied;
                    if (remaining <= 0) continue;

                    yield return new GraceTarget(remaining, verdict.OutOfTotal, null, subjectResult);
                    continue;
                }

                foreach (var sm in group.Where(sm => sm.Marks.HasValue && !SubjectPassEvaluator.IsHeadPassed(sm)))
                {
                    if (!spec.MatchesHead(sm)) continue;

                    // A resolved head is skipped; the subject's other heads stay eligible.
                    if (IsResolved(sm)) continue;

                    var required = SubjectPassEvaluator.GetHeadPass(sm) - (sm.Marks ?? 0);
                    yield return new GraceTarget(required, SubjectPassEvaluator.GetHeadOutOf(sm), sm, null);
                }
            }
        }

        /// <summary>True when marks entry resolved (^) this head, by amount or by the symbol.</summary>
        private static bool IsResolved(StudentMarks sm) =>
            (sm.Resolution ?? 0) > 0 || (sm.Grace?.Contains(ResolutionSymbol) ?? false);

        /// <summary>Returns false, changing nothing, when the target must not take grace.</summary>
        private static bool ApplyGrace(GraceTarget target, string? symbol)
        {
            if (target.Head is StudentMarks sm)
            {
                // Never overwrite a resolution (^): the symbol would vanish from every report.
                if (IsResolved(sm)) return false;

                sm.Marks = (sm.RawMarks ?? 0) + (sm.Resolution ?? 0) + target.Required;
                sm.Grace = target.Required.ToString() + (symbol ?? string.Empty);
                return true;
            }

            // Combined: accumulate, so a second rule that legitimately adds more keeps the first award.
            var result = target.SubjectResult!;
            result.GraceApplied += target.Required;
            result.GraceSymbol = MergeSymbols(result.GraceSymbol, symbol);
            return true;
        }

        private static string? MergeSymbols(string? existing, string? added)
        {
            if (string.IsNullOrEmpty(added)) return existing;
            if (string.IsNullOrEmpty(existing)) return added;
            if (existing.Contains(added)) return existing;

            var merged = existing + added;
            return merged.Length <= GraceSymbolMaxLength ? merged : merged[..GraceSymbolMaxLength];
        }

        /// <summary>
        /// Resolves one of the action's two limits. <paramref name="unitOutOf"/> is the out-of of
        /// whatever is being graced -- the head for a head-wise subject, the whole subject for a
        /// combined one.
        /// </summary>
        private static decimal CalculateActionLimit(string? paramType, decimal? value, int aggregateOutOf, int unitOutOf, bool blankMeansUnlimited, string? expressionStr = null)
        {
            var normalizedType = NormalizeKey(paramType);

            // An explicit "None" means "no cap from this parameter" in either slot. A blank type
            // is read that way only for the second parameter; a blank first parameter still
            // yields 0 so a half-configured rule grants nothing.
            if (normalizedType == "NONE") return decimal.MaxValue;
            if (string.IsNullOrEmpty(normalizedType))
            {
                return blankMeansUnlimited ? decimal.MaxValue : 0;
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

        private static string NormalizeKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        }
    }
}
