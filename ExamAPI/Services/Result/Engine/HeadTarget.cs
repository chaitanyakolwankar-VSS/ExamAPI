using System;
using System.Collections.Generic;
using System.Linq;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine
{
    /// <summary>
    /// Parses <see cref="RuleAction.Target"/> -- the comma-separated scope string a rule author
    /// types in the Ordinance UI -- into something callers can test against.
    /// <para>
    /// A target string mixes two kinds of token:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Subject scope</b> -- which subjects qualify, by their computed status:
    /// <c>AllSubjects</c>, <c>FailingSubjects</c>, <c>PassingSubjects</c>, <c>AbsentSubjects</c>,
    /// <c>NotAttempted</c>. No scope token means every subject.</item>
    /// <item><b>Head names</b> -- anything else, e.g. <c>ESE</c>. No head token means every head.</item>
    /// </list>
    /// <para>
    /// Head tokens match the SEMANTIC label (<see cref="SubjectCredits.HeadType"/>, what the
    /// gazette, marksheet and marks-entry grid all print) and also the positional key
    /// (<see cref="StudentMarks.Head"/>, "H1"/"H2"). The Ordinance UI tells authors to type the
    /// configured head name, so the label has to be the primary match -- the existing handlers
    /// compare only the positional key, which is why a target of "ESE" silently matches nothing
    /// there. Accepting both spellings means no existing rule changes meaning.
    /// </para>
    /// </summary>
    public sealed class HeadTargetSpec
    {
        /// <summary>Subject statuses in scope. Empty means every status.</summary>
        public IReadOnlyList<string> SubjectStatuses { get; }

        /// <summary>Normalised head tokens. Empty means every head.</summary>
        public IReadOnlyList<string> HeadTokens { get; }

        private HeadTargetSpec(IReadOnlyList<string> subjectStatuses, IReadOnlyList<string> headTokens)
        {
            SubjectStatuses = subjectStatuses;
            HeadTokens = headTokens;
        }

        /// <summary>Every subject, every head.</summary>
        public static HeadTargetSpec All { get; } = new(Array.Empty<string>(), Array.Empty<string>());

        /// <summary>True when the author narrowed the target to specific heads.</summary>
        public bool RestrictsHeads => HeadTokens.Count > 0;

        /// <summary>True when the author narrowed the target to specific subject statuses.</summary>
        public bool RestrictsSubjects => SubjectStatuses.Count > 0;

        public static HeadTargetSpec ForStatuses(params string[] statuses) =>
            new(statuses.ToArray(), Array.Empty<string>());

        /// <summary>
        /// Splits a target string. Unknown tokens are treated as head names, which is what the
        /// Ordinance UI's placeholder tells authors to type.
        /// </summary>
        public static HeadTargetSpec Parse(string? target)
        {
            if (string.IsNullOrWhiteSpace(target)) return All;

            var statuses = new List<string>();
            var heads = new List<string>();
            var everySubject = false;

            foreach (var raw in target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var key = NormalizeKey(raw);
                if (key.Length == 0) continue;

                switch (key)
                {
                    // "every subject" -- the vocabulary both existing handlers already accept.
                    // Parsing continues: "AllSubjects,ESE" means every subject, ESE head only.
                    case "ALL":
                    case "ALLSUBJECTS":
                    case "SUBJECT":
                    case "SUBJECTS":
                        everySubject = true;
                        break;

                    case "FAILINGSUBJECTS":
                    case "FAILINGSUBJECT":
                    case "FAILEDSUBJECTS":
                    case "FAILEDSUBJECT":
                    case "FAILINGHEADS":
                    case "FAILINGHEAD":
                        Add(statuses, SubjectStatuses_Failed);
                        break;

                    case "PASSINGSUBJECTS":
                    case "PASSINGSUBJECT":
                    case "PASSEDSUBJECTS":
                    case "PASSEDSUBJECT":
                        Add(statuses, SubjectStatuses_Passed);
                        break;

                    case "ABSENTSUBJECTS":
                    case "ABSENTSUBJECT":
                    case "ABSENT":
                        Add(statuses, SubjectStatuses_Absent);
                        break;

                    case "NOTATTEMPTED":
                    case "NOTATTEMPTEDSUBJECTS":
                        Add(statuses, StatusNotAttempted);
                        break;

                    default:
                        Add(heads, key);
                        break;
                }
            }

            return new HeadTargetSpec(everySubject ? Array.Empty<string>() : statuses, heads);
        }

        /// <summary>Whether a subject with this computed status is in scope.</summary>
        public bool MatchesStatus(string? status) =>
            SubjectStatuses.Count == 0 ||
            SubjectStatuses.Contains(NormalizeStatus(status), StringComparer.Ordinal);

        /// <summary>Whether a head is in scope, matched on its label or its positional key.</summary>
        public bool MatchesHead(string? head, string? headType)
        {
            if (HeadTokens.Count == 0) return true;

            var labelKey = NormalizeKey(headType);
            var headKey = NormalizeKey(head);

            return (labelKey.Length > 0 && HeadTokens.Contains(labelKey, StringComparer.Ordinal))
                || (headKey.Length > 0 && HeadTokens.Contains(headKey, StringComparer.Ordinal));
        }

        /// <summary>Whether a stored mark's head is in scope.</summary>
        public bool MatchesHead(StudentMarks sm) =>
            MatchesHead(sm.Head, SubjectPassEvaluator.GetHeadLabel(sm));

        /// <summary>
        /// Combines two scopes permissively -- used when several rules each grant assignment.
        /// An unrestricted side wins, because "all" already includes whatever the other names.
        /// </summary>
        public HeadTargetSpec Union(HeadTargetSpec other)
        {
            var statuses = SubjectStatuses.Count == 0 || other.SubjectStatuses.Count == 0
                ? Array.Empty<string>()
                : SubjectStatuses.Union(other.SubjectStatuses, StringComparer.Ordinal).ToArray();

            var heads = HeadTokens.Count == 0 || other.HeadTokens.Count == 0
                ? Array.Empty<string>()
                : HeadTokens.Union(other.HeadTokens, StringComparer.Ordinal).ToArray();

            return new HeadTargetSpec(statuses, heads);
        }

        /// <summary>The scope tokens, for echoing the effective configuration back to a UI.</summary>
        public IReadOnlyList<string> DescribeStatuses() => SubjectStatuses;

        public IReadOnlyList<string> DescribeHeads() => HeadTokens;

        // Status constants kept local so this type does not depend on the order in which
        // SubjectStatuses is initialised.
        private const string SubjectStatuses_Failed = "FAILED";
        private const string SubjectStatuses_Passed = "PASSED";
        private const string SubjectStatuses_Absent = "ABSENT";
        private const string StatusNotAttempted = "NOTATTEMPTED";

        private static void Add(List<string> list, string value)
        {
            if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value);
        }

        private static string NormalizeStatus(string? status) => NormalizeKey(status);

        /// <summary>
        /// Uppercase, alphanumerics only -- the same normalisation the ordinance engine already
        /// uses for exam types and action targets, so "ESE (TH)", "ese-th" and "ESETH" agree.
        /// </summary>
        public static string NormalizeKey(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }
}
