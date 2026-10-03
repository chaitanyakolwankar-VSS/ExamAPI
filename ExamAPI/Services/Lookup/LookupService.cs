using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Lookup
{
    /// <summary>
    /// Read-only lookup data used by many screens. Tenant scoping comes from the global query
    /// filters (the caller's CollegeId claim), exactly like the per-screen lookups it replaces.
    /// </summary>
    public class LookupService : ILookupService
    {
        private const int MaxSemesters = 10;
        private static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        /// <summary>The one semester list: Sem-1..Sem-10 / Semester I..X.</summary>
        public static readonly IReadOnlyList<LookupSemesterDto> Semesters =
            Enumerable.Range(1, MaxSemesters)
                .Select(n => new LookupSemesterDto { Value = $"Sem-{n}", Label = $"Semester {Roman[n - 1]}", Number = n })
                .ToList();

        private readonly ApplicationDbContext _context;
        private readonly ICurrentUser? _currentUser;

        public LookupService(ApplicationDbContext context, ICurrentUser? currentUser = null)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<LookupBootstrapDto> GetBootstrapAsync(CancellationToken ct = default)
        {
            var dto = new LookupBootstrapDto { Semesters = Semesters.ToList() };

            dto.AcademicYears = await _context.AcademicYears.AsNoTracking()
                // Newest first. ShortDuration ("2025-2026") sorts correctly; FullDuration starts with a
                // day/month ("01/June/2025-...") so it is only the tie-breaker.
                .OrderByDescending(a => a.ShortDuration)
                .ThenByDescending(a => a.FullDuration)
                .Select(a => new LookupAcademicYearDto
                {
                    Ayid = a.AYID,
                    FullDuration = a.FullDuration,
                    ShortDuration = a.ShortDuration ?? "",
                    IsCurrent = a.IsCurrent,
                })
                .ToListAsync(ct);

            dto.Courses = await _context.CourseMasters.AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new LookupCourseDto { CourseId = c.CourseId, Name = c.Name, Code = c.CourseCode })
                .ToListAsync(ct);

            dto.Patterns = await _context.PatternMasters.AsNoTracking()
                .OrderBy(p => p.PatternName)
                .Select(p => new LookupPatternDto { PatternId = p.PatternId, Name = p.PatternName })
                .ToListAsync(ct);

            dto.GradeMasters = await _context.GradeMasters.AsNoTracking()
                .OrderBy(g => g.Name)
                .Select(g => new LookupGradeMasterDto { GradeMasterId = g.GradeMasterId, Name = g.Name })
                .ToListAsync(ct);

            // A platform administrator has no college (and the College filter lets them see all
            // of them), so there is no single "own" college to return.
            var collegeId = _currentUser?.CollegeId;
            if (collegeId.HasValue)
            {
                dto.College = await _context.Colleges.AsNoTracking()
                    .Where(c => c.CollegeId == collegeId.Value)
                    .Select(c => new LookupCollegeDto
                    {
                        CollegeId = c.CollegeId,
                        Name = c.Name,
                        Code = c.CollegeCode,
                        Centre = c.CollegeCenter,
                        HasLogo = c.LogoUrl != null && c.LogoUrl != "",
                        HasBanner = c.LogoBannerUrl != null && c.LogoBannerUrl != "",
                    })
                    .FirstOrDefaultAsync(ct);
            }

            return dto;
        }

        public async Task<List<LookupExamDto>?> GetExamsAsync(
            Guid courseId, Guid ayid, string purpose, string? semester, CancellationToken ct = default)
        {
            var key = ExamPurposes.Normalize(purpose);
            if (key == null) return null;

            var query = ExamPurposes.Query(_context, key, courseId, ayid, semester);
            if (query == null) return null;

            return await query.AsNoTracking()
                .OrderBy(e => e.Name)
                .Select(e => new LookupExamDto
                {
                    ExamId = e.ExamId,
                    Name = e.Name,
                    DisplayName = e.RevaluationForExamId != null ? e.Name + " (Revaluation)" : e.Name,
                    ExamType = e.ExamType,
                    IsActive = e.IsActive == true,
                    IsRevaluation = e.RevaluationForExamId != null,
                    RevaluationForExamId = e.RevaluationForExamId,
                    IsLocked = e.IsLocked,
                })
                .ToListAsync(ct);
        }

        public async Task<List<LookupSubjectDto>> GetSubjectsAsync(
            Guid courseId, string? pattern, string? semester, CancellationToken ct = default)
        {
            // Same predicate as SubjectService.GetSubjectsAsync.
            return await _context.SubjectMasters.AsNoTracking()
                .Where(s => s.CourseId == courseId && s.Pattern == pattern && s.SemId == semester)
                .OrderBy(s => s.SubjectCode)
                .Select(s => new LookupSubjectDto { SubjectId = s.SubjectId, Name = s.Name, Code = s.SubjectCode })
                .ToListAsync(ct);
        }
    }
}
