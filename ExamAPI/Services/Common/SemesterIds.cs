namespace ExamAPI.Services.Common
{
    /// <summary>
    /// SemesterId is a free string such as "Sem-6". Comparing it lexicographically breaks at
    /// "Sem-10" ("Sem-10" &lt; "Sem-9"), so compare the trailing number instead.
    /// </summary>
    public static class SemesterIds
    {
        /// <summary>Trailing integer of the id ("Sem-10" -> 10), or null if there is none.</summary>
        public static int? TrailingNumber(string? semesterId)
        {
            if (string.IsNullOrWhiteSpace(semesterId)) return null;

            var s = semesterId.Trim();
            var end = s.Length;
            var start = end;
            while (start > 0 && char.IsDigit(s[start - 1])) start--;

            return start < end && int.TryParse(s.AsSpan(start, end - start), out var n) ? n : null;
        }

        /// <summary>
        /// Numeric comparison of the trailing semester numbers; falls back to the ordinal string
        /// comparison when either side has no number.
        /// </summary>
        public static int Compare(string? a, string? b)
        {
            var na = TrailingNumber(a);
            var nb = TrailingNumber(b);
            return na.HasValue && nb.HasValue
                ? na.Value.CompareTo(nb.Value)
                : string.Compare(a, b, StringComparison.Ordinal);
        }
    }
}
