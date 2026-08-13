namespace ExamAPI.Models
{
    /// <summary>
    /// The two kinds of follow-up attempt a student can be assigned into, from the
    /// ATKT / Revaluation assignment screen.
    /// <para>
    /// This is the screen's own mode switch, not a stored value -- which rules apply is
    /// resolved from <see cref="RuleSet.ExamType"/>, not from this.
    /// </para>
    /// </summary>
    public static class AssignmentModes
    {
        /// <summary>Allowed To Keep Term -- the backlog / supplementary attempt.</summary>
        public const string Atkt = "ATKT";

        /// <summary>Revaluation of an already-conducted attempt.</summary>
        public const string Revaluation = "Revaluation";

        public static bool IsRevaluation(string? mode) =>
            string.Equals(mode, Revaluation, StringComparison.OrdinalIgnoreCase);
    }
}
