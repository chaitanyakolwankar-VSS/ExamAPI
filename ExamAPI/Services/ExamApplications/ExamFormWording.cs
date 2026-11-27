using ExamAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.ExamApplications
{
    /// <summary>The declaration and note printed on a college's exam application form (College settings, built-in defaults).</summary>
    public sealed record ExamFormText(string Declaration, string? Note);

    public static class ExamFormWording
    {
        /// <summary>From the old LTCE form, without the college's name.</summary>
        public const string DefaultDeclaration =
            "I hereby declare that since my last appearance at this examination from this college, I have not joined any other college "
            + "for these studies, have not appeared at this examination of any other university, and have not joined any other "
            + "university for this or any other course.";

        public const string DefaultNote =
            "Attach photocopies of all previous semester marksheets; otherwise the result may be kept back (R.L.E.).";

        public static async Task<ExamFormText> LoadAsync(ApplicationDbContext db, Guid? collegeId, CancellationToken ct = default)
        {
            var s = collegeId == null ? null : await db.CollegeSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CollegeId == collegeId)
                .Select(x => new { x.ExamFormDeclaration, x.ExamFormNote })
                .FirstOrDefaultAsync(ct);
            var declaration = string.IsNullOrWhiteSpace(s?.ExamFormDeclaration) ? DefaultDeclaration : s.ExamFormDeclaration.Trim();
            var note = s?.ExamFormNote == null ? DefaultNote : s.ExamFormNote.Trim() == "" ? null : s.ExamFormNote.Trim();
            return new ExamFormText(declaration, note);
        }
    }
}
