using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Lookup;
using Microsoft.EntityFrameworkCore;


namespace ExamAPI.Services.DeclareResult
{
    public class DResultService : IDResultService
    {
        private readonly ApplicationDbContext _context;
        private readonly IGenericRepository _genericRepository;

        public DResultService(ApplicationDbContext context, IGenericRepository genericRepository)
        {
            _context = context;
            _genericRepository = genericRepository;
        }

        /// <summary>
        /// The exams a Declare Result / Release Hall Ticket screen may list for a course + academic year +
        /// semester + pattern. Starts from the shared ExamMaster filter (<see cref="ExamPurposes"/>) and keeps
        /// the exams that have students in that semester/pattern (a MarksMaster row) or already have a
        /// DeclareResult row for it. ExamMaster.Semester is never written, so the semester comes from the
        /// request, MarksMaster.SemesterId and DeclareResult.Sem_id. No DeclareResult row is needed: a fresh
        /// college lists its exams as "not declared / not released".
        /// </summary>
        internal static IQueryable<ExamMaster> EligibleExams(
            ApplicationDbContext context, string purpose, Guid courseId, Guid ayid, string? semester, string? pattern)
        {
            var exams = ExamPurposes.Query(context, purpose, courseId, ayid, null)
                        ?? context.Exams.Where(_ => false);

            var withRow = context.DeclareResults
                .Where(dr => dr.AcademicYear == ayid && dr.CourseId == courseId && dr.Sem_id == semester && dr.Pattern == pattern)
                .Select(dr => dr.ExamId);
            var withStudents = context.MarksMasters
                .Where(m => m.AcademicYearAYID == ayid && m.SemesterId == semester && m.Pattern == pattern && m.ExamId != null)
                .Select(m => m.ExamId!.Value);

            return exams.Where(e => withRow.Contains(e.ExamId) || withStudents.Contains(e.ExamId));
        }

        /// <summary>The live DeclareResult row (if any) of each exam for one course/AY/semester/pattern.</summary>
        internal static async Task<Dictionary<Guid, ExamAPI.Models.DeclareResult>> RowsByExam(
            ApplicationDbContext context, Guid courseId, Guid ayid, string? semester, string? pattern, IEnumerable<Guid> examIds)
        {
            var ids = examIds.ToList();
            var rows = await context.DeclareResults
                .Where(dr => dr.AcademicYear == ayid && dr.CourseId == courseId && dr.Sem_id == semester
                             && dr.Pattern == pattern && ids.Contains(dr.ExamId))
                .OrderBy(dr => dr.CreatedAt)
                .ToListAsync();
            // Older databases may hold duplicate rows (the hall-ticket GET used to insert them); the first wins.
            return rows.GroupBy(r => r.ExamId).ToDictionary(g => g.Key, g => g.First());
        }

        /// <summary>
        /// Records a report run (bulk marksheet, gazette) on the exam's DeclareResult row, creating the row when
        /// none exists yet. Course and academic year come from the exam itself. The caller saves.
        /// </summary>
        public static async Task RecordGenerationAsync(
            ApplicationDbContext context, ExamMaster exam, string semester, string pattern, Action<ExamAPI.Models.DeclareResult> apply)
        {
            if (exam.CourseId is not Guid courseId || exam.AcademicYearAYID is not Guid ayid) return;

            var row = (await RowsByExam(context, courseId, ayid, semester, pattern, new[] { exam.ExamId }))
                .GetValueOrDefault(exam.ExamId);
            if (row == null)
            {
                row = new ExamAPI.Models.DeclareResult
                {
                    DeclareID = Guid.NewGuid(),
                    CollegeId = exam.CollegeId,
                    ExamId = exam.ExamId,
                    CourseId = courseId,
                    AcademicYear = ayid,
                    Sem_id = semester,
                    Pattern = pattern,
                };
                context.DeclareResults.Add(row);
            }
            apply(row);
        }

        private static string DisplayName(ExamMaster e) =>
            e.RevaluationForExamId != null ? e.Name + " (Revaluation)"
            : ExamTypeKeys.IsAtkt(e.ExamType) ? e.Name + " (A.T.K.T)"
            : e.Name;

        public async Task<List<DeclareResultDTO>> GetExam(GetDeclareExam dto)
        {
            var exams = await EligibleExams(_context, ExamPurposes.All, dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern)
                .OrderBy(e => e.Name)
                .ToListAsync();
            var rows = await RowsByExam(_context, dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern, exams.Select(e => e.ExamId));

            return exams.Select(em => ToDto(em, rows.GetValueOrDefault(em.ExamId), dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern)).ToList();
        }

        public async Task<List<DeclareResultDTO>> GetTableExam(DeclareExamTable dto)
        {
            var exams = await EligibleExams(_context, ExamPurposes.All, dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern)
                .Where(e => e.ExamId == dto.ExamId)
                .ToListAsync();
            var rows = await RowsByExam(_context, dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern, exams.Select(e => e.ExamId));

            return exams.Select(em => ToDto(em, rows.GetValueOrDefault(em.ExamId), dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern)).ToList();
        }

        private static DeclareResultDTO ToDto(ExamMaster em, ExamAPI.Models.DeclareResult? dr, Guid courseId, Guid ayid, string semester, string pattern) =>
            new()
            {
                ExamId = em.ExamId,
                CourseId = courseId,
                Ayid = ayid,
                Semester = semester,
                Pattern = pattern,
                Examname = DisplayName(em),
                // No row yet means "not declared".
                IsDeclare = dr?.IsDeclare ?? false,
                DeclareDate = dr?.DeclareDate,
                HasRecord = dr != null,
                MarksheetGenerated = (dr?.ResDeclare ?? 0) > 0,
                GazetteGenerated = (dr?.GazetteGnrt ?? 0) > 0,
            };

        public async Task<bool> ToggleDeclare(ToggleDeclareResultDTO dto)
        {
            if (dto.IsDeclare && (dto.DeclareDate == null || dto.DeclareDate == default(DateTime)))
                throw new ArgumentException("Declare Date is required while declaring a result");

            // The exam must be one this screen can list (same course, academic year, semester/pattern);
            // the tenant filter keeps other colleges' exams out.
            var exam = await EligibleExams(_context, ExamPurposes.All, dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern)
                .FirstOrDefaultAsync(e => e.ExamId == dto.ExamId);
            if (exam == null) return false;

            var existing = (await RowsByExam(_context, dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern, new[] { dto.ExamId }))
                .GetValueOrDefault(dto.ExamId);

            // A result is declared only after its marksheets have been generated (owner, 2026-10-04).
            // Withdrawing a declaration is always allowed.
            if (dto.IsDeclare && (existing?.ResDeclare ?? 0) == 0)
                throw new InvalidOperationException("Generate the marksheets for this exam before declaring the result.");

            if (existing != null)
            {
                existing.IsDeclare = dto.IsDeclare;
                existing.DeclareDate = dto.IsDeclare ? dto.DeclareDate : null;
            }
            else
            {
                _context.DeclareResults.Add(new ExamAPI.Models.DeclareResult
                {
                    DeclareID = Guid.NewGuid(),
                    CollegeId = exam.CollegeId,
                    ExamId = dto.ExamId,
                    CourseId = dto.CourseId,
                    AcademicYear = dto.Ayid,
                    Sem_id = dto.Semester,
                    Pattern = dto.Pattern,
                    IsDeclare = dto.IsDeclare,
                    DeclareDate = dto.IsDeclare ? dto.DeclareDate : null
                });
            }

            await _context.SaveChangesAsync();
            return true;
        }
    }
}
