using ExamAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Common
{
    /// <summary>
    /// Exams are stored per branch (one ExamMaster row per course); "the exam" the exam office thinks of is the group
    /// of rows with the same name, type and academic year. Revaluation exams only group with revaluation exams.
    /// </summary>
    public static class ExamGroups
    {
        /// <summary>Every branch's copy of the exam, the given one included. Empty when the exam is not found.</summary>
        public static async Task<List<Guid>> CopyIdsAsync(ApplicationDbContext db, Guid examId)
        {
            var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.ExamId == examId);
            if (exam == null) return new List<Guid>();

            var isReval = exam.RevaluationForExamId != null;
            var copies = db.Exams.AsNoTracking()
                .Where(e => e.Name == exam.Name && e.ExamType == exam.ExamType && e.AcademicYearAYID == exam.AcademicYearAYID);
            copies = isReval
                ? copies.Where(e => e.RevaluationForExamId != null)
                : copies.Where(e => e.RevaluationForExamId == null);
            var ids = await copies.Select(e => e.ExamId).ToListAsync();
            if (!ids.Contains(exam.ExamId)) ids.Add(exam.ExamId);
            return ids;
        }

        /// <summary>The copy that stands for the group: the earliest created, then the smallest ID. Null when the exam is not found.</summary>
        public static async Task<Guid?> CanonicalIdAsync(ApplicationDbContext db, Guid examId)
        {
            var ids = await CopyIdsAsync(db, examId);
            if (ids.Count == 0) return null;
            var copies = await db.Exams.AsNoTracking().Where(e => ids.Contains(e.ExamId))
                .Select(e => new { e.ExamId, e.CreatedAt }).ToListAsync();
            return copies.OrderBy(e => e.CreatedAt).ThenBy(e => e.ExamId).First().ExamId;
        }
    }
}
