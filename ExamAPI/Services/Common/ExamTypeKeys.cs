using ExamAPI.Services.Result.Engine;

namespace ExamAPI.Services.Common
{
    /// <summary>
    /// Exam-type comparison keys, tolerant of the ATKT spelling variants that exist in live
    /// data: Exam Master authors "A.T.K.T", historical/seeded data uses "KT" or "ATKT". All
    /// collapse to "ATKT". Minimal on purpose -- the proper fix (an ExamTypeMaster keyed by id)
    /// is deferred: see ATKT-15 in the ATKT/Revaluation task register.
    /// </summary>
    public static class ExamTypeKeys
    {
        public const string Atkt = "ATKT";

        /// <summary>Uppercase alphanumerics; "KT" and "ATKT" (any punctuation) become "ATKT".</summary>
        public static string Canonical(string? examType)
        {
            var key = HeadTargetSpec.NormalizeKey(examType);
            return key is "KT" or "ATKT" ? Atkt : key;
        }

        /// <summary>True for "KT", "ATKT", "A.T.K.T" and any case/punctuation variant.</summary>
        public static bool IsAtkt(string? examType) => Canonical(examType) == Atkt;
    }
}
