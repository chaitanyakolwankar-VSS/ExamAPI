using ExamAPI.Models;

namespace ExamAPI.Services.ExamApplications
{
    public class FeeLine
    {
        public string Label { get; set; } = "";
        public decimal Amount { get; set; }
    }

    /// <summary>What a student pays and why: shown on the student page, stored on the application, printed on the receipt.</summary>
    public class FeeBreakdown
    {
        public List<FeeLine> Lines { get; set; } = new();
        public decimal Total { get; set; }
        public bool IsExempt { get; set; }
        public bool IsLate { get; set; }
        /// <summary>The category rate used, or null for the exam's own fee.</summary>
        public string? Category { get; set; }
    }

    /// <summary>
    /// The only place an exam application fee is worked out. Rules: per-subject fee × subjects (capped by MaxSubjectFee),
    /// plus the fixed fee, plus the late fee from LateFeeFrom. A category row replaces the per-subject and/or fixed fee,
    /// or makes the fee nil (exempt; no late fee either).
    /// </summary>
    public static class FeeCalculator
    {
        public static FeeBreakdown Compute(ExamFeeSetup setup, string? studentCategory, int subjectCount, DateTime nowUtc)
        {
            if (subjectCount < 0) throw new ArgumentOutOfRangeException(nameof(subjectCount));
            var rate = FindRate(setup, studentCategory);
            var result = new FeeBreakdown { Category = rate?.Category };

            if (rate?.IsExempt == true)
            {
                result.IsExempt = true;
                result.Lines.Add(new FeeLine { Label = $"Fee waived ({rate.Category})", Amount = 0 });
                return result;
            }

            var perSubject = rate?.PerSubjectFee ?? setup.PerSubjectFee;
            var fixedFee = rate?.FixedFee ?? setup.FixedFee;

            var subjectsPart = perSubject * subjectCount;
            var capped = setup.MaxSubjectFee is { } cap && subjectsPart > cap;
            if (capped) subjectsPart = setup.MaxSubjectFee!.Value;
            var noun = setup.Kind == ExamApplicationKinds.Revaluation ? "paper" : "subject";
            if (subjectsPart > 0 || subjectCount > 0)
            {
                result.Lines.Add(new FeeLine
                {
                    Label = capped
                        ? $"{subjectCount} {noun}{(subjectCount == 1 ? "" : "s")} (at most ₹{Money(setup.MaxSubjectFee!.Value)})"
                        : $"{subjectCount} {noun}{(subjectCount == 1 ? "" : "s")} × ₹{Money(perSubject)}",
                    Amount = subjectsPart,
                });
            }

            if (fixedFee > 0)
                result.Lines.Add(new FeeLine { Label = string.IsNullOrWhiteSpace(setup.FixedFeeLabel) ? "Fixed fee" : setup.FixedFeeLabel.Trim(), Amount = fixedFee });

            if (setup.LateFeeFrom is { } lateFrom && nowUtc >= lateFrom && setup.LateFee > 0)
            {
                result.IsLate = true;
                result.Lines.Add(new FeeLine { Label = "Late fee", Amount = setup.LateFee });
            }

            result.Total = result.Lines.Sum(l => l.Amount);
            return result;
        }

        public static ExamFeeCategoryRate? FindRate(ExamFeeSetup setup, string? studentCategory)
        {
            var wanted = NormalizeCategory(studentCategory);
            if (wanted == "") return null;
            return setup.CategoryRates.FirstOrDefault(r => NormalizeCategory(r.Category) == wanted);
        }

        public static string NormalizeCategory(string? category) => (category ?? "").Trim().ToUpperInvariant();

        /// <summary>₹ amounts without trailing ".00".</summary>
        public static string Money(decimal amount) =>
            amount == decimal.Truncate(amount) ? amount.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                                               : amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }
}
