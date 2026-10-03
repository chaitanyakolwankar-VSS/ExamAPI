using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine
{
    /// <summary>
    /// The single place that turns a stored <see cref="RuleCondition"/> into a boolean.
    /// <para>
    /// Result processing uses it to decide whether an ordinance rule fires; exam assignment
    /// uses it to decide whether a student is eligible. Both read the same rows, the same
    /// facts and the same operator vocabulary, so a rule authored in the Ordinance UI means
    /// the same thing wherever it is evaluated.
    /// </para>
    /// </summary>
    public static class RuleConditionEvaluator
    {
        /// <summary>
        /// Compares a fact value against a stored condition value. Both the symbol form and the
        /// word form of every operator are accepted -- the Ordinance UI has written both.
        /// An unparseable or unknown operand yields false rather than throwing, so one bad row
        /// cannot take down a whole result run.
        /// </summary>
        public static bool Compare(double factValue, string? op, string? targetValueStr)
        {
            if (!double.TryParse(targetValueStr, out double targetValue)) return false;

            return op switch
            {
                "Equals" or "==" => factValue == targetValue,
                "GreaterThan" or ">" => factValue > targetValue,
                "LessThan" or "<" => factValue < targetValue,
                "GreaterOrEqual" or "GreaterThanOrEqual" or ">=" => factValue >= targetValue,
                "LessOrEqual" or "LessThanOrEqual" or "<=" => factValue <= targetValue,
                "NotEquals" or "!=" => factValue != targetValue,
                _ => false
            };
        }

        /// <summary>
        /// Evaluates every condition on a rule (AND). A rule with no conditions is vacuously true.
        /// </summary>
        /// <param name="throwOnMissingFact">
        /// Result processing throws on an unknown fact so a mis-authored ordinance cannot silently
        /// under-award. Eligibility screening passes false: an unknown fact there should exclude
        /// nobody, it should just not constrain the list.
        /// </param>
        public static async Task<bool> EvaluateRuleAsync(
            EngineRegistry registry,
            Rule rule,
            StudentMaster? student,
            MarksMaster marksMaster,
            bool throwOnMissingFact = true)
        {
            var conditions = rule.Conditions;
            if (conditions == null || conditions.Count == 0) return true;

            foreach (var condition in conditions)
            {
                var provider = registry.GetFactProvider(condition.FactName ?? string.Empty);
                if (provider == null)
                {
                    if (throwOnMissingFact)
                    {
                        throw new InvalidOperationException(
                            $"Fact provider for '{condition.FactName}' not found. Rule evaluation aborted.");
                    }
                    continue;
                }

                double factValue = await provider.GetValueAsync(student, marksMaster);
                if (!Compare(factValue, condition.Operator, condition.Value)) return false;
            }

            return true;
        }

        /// <summary>
        /// Eligibility gate: every enabled rule in the set must hold. An empty or absent set
        /// admits everyone, which is what a college with no extra policy expects.
        /// </summary>
        public static async Task<bool> IsEligibleAsync(
            EngineRegistry registry,
            IEnumerable<Rule>? rules,
            StudentMaster? student,
            MarksMaster marksMaster)
        {
            if (rules == null) return true;

            foreach (var rule in rules.Where(r => r.IsEnabled && !r.IsDeleted).OrderBy(r => r.Priority))
            {
                if (!await EvaluateRuleAsync(registry, rule, student, marksMaster, throwOnMissingFact: false))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
