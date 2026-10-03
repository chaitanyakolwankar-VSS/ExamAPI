using ExamAPI.Data;
using ExamAPI.Models;

namespace ExamAPI.Services.Lookup
{
    /// <summary>
    /// The one place that says which exams each screen's drop-down shows. Each purpose
    /// reproduces the predicate that used to be copy-pasted into a per-screen service; the
    /// old endpoints can delegate here. Tenant isolation and soft-delete come from the
    /// global query filters, as everywhere else.
    /// </summary>
    public static class ExamPurposes
    {
        /// <summary>Exam Master list: every non-deleted exam, active or not.</summary>
        public const string Master = "master";
        /// <summary>Regular exam conduct: active, Regular type, not a revaluation.</summary>
        public const string Regular = "regular";
        /// <summary>Post-assignment screens: active exams of every type incl. revaluation.</summary>
        public const string All = "all";
        /// <summary>Assign Seat No: active, non-revaluation, with marks rows for semester + AY.</summary>
        public const string SeatNo = "seatNo";
        /// <summary>Hall ticket: active, non-revaluation.</summary>
        public const string HallTicket = "hallTicket";
        /// <summary>
        /// Result processing / Apply Grace Marks: course + AY, active and inactive. No Semester
        /// filter -- ExamMaster.Semester is never written (T-25).
        /// </summary>
        public const string Process = "process";

        public static readonly IReadOnlyList<string> Names =
            new[] { Master, Regular, All, SeatNo, HallTicket, Process };

        /// <summary>Canonical (correctly cased) purpose name, or null when unknown.</summary>
        public static string? Normalize(string? purpose) =>
            Names.FirstOrDefault(n => string.Equals(n, purpose?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static bool RequiresSemester(string purpose) => purpose == SeatNo;

        /// <summary>
        /// The filtered exam query for a purpose, or null when the purpose is unknown.
        /// <paramref name="purpose"/> must already be normalized.
        /// </summary>
        public static IQueryable<ExamMaster>? Query(
            ApplicationDbContext context, string purpose, Guid courseId, Guid ayid, string? semester)
        {
            var byCourseAy = context.Exams.Where(a => a.CourseId == courseId && a.AcademicYearAYID == ayid);

            switch (purpose)
            {
                case Master:
                    return byCourseAy;
                case Regular:
                    return byCourseAy.Where(a => a.IsActive == true && a.ExamType == "Regular" && a.RevaluationForExamId == null);
                case All:
                    return byCourseAy.Where(a => a.IsActive == true);
                case SeatNo:
                    var withMarks = context.MarksMasters
                        .Where(m => m.SemesterId == semester && m.AcademicYearAYID == ayid)
                        .Select(m => m.ExamId);
                    return byCourseAy.Where(a => a.IsActive == true && a.RevaluationForExamId == null && withMarks.Contains(a.ExamId));
                case HallTicket:
                    return byCourseAy.Where(a => a.IsActive == true && a.RevaluationForExamId == null);
                case Process:
                    return byCourseAy;
                default:
                    return null;
            }
        }
    }
}
