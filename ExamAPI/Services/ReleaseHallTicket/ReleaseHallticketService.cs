using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Common;
using ExamAPI.Services.DeclareResult;
using ExamAPI.Services.Lookup;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.ReleaseHallTicket
{
    public class ReleaseHallticketService : IReleaseHallticketService
    {
        private readonly ApplicationDbContext _context;
        private readonly IGenericRepository _genericRepository;

        public ReleaseHallticketService(ApplicationDbContext context, IGenericRepository genericRepository)
        {
            _context = context;
            _genericRepository = genericRepository;
        }

        /// <summary>Exams that can have a hall ticket (active, not a revaluation) for the semester, with their release status.</summary>
        public async Task<List<DeclareHallTicketDTO>> GetExam(GetDeclareExam dto)
        {
            var exams = await DResultService.EligibleExams(_context, ExamPurposes.HallTicket, dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern)
                .OrderBy(e => e.Name)
                .ToListAsync();
            var rows = await DResultService.RowsByExam(_context, dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern, exams.Select(e => e.ExamId));

            return exams.Select(em =>
            {
                var dr = rows.GetValueOrDefault(em.ExamId);
                return new DeclareHallTicketDTO
                {
                    ExamId = em.ExamId,
                    Examname = em.Name,
                    CourseId = dto.CourseId,
                    Ayid = dto.Ayid,
                    Semester = dto.Semester,
                    Pattern = dto.Pattern,
                    // No row yet means "not released".
                    ReleaseHallTicket = dr?.ReleaseHallTicket ?? false,
                    HallTicketDeclareDate = dr?.HallTicketDeclareDate,
                    HallTicketUpdatedAt = dr?.HallTicketUpdatedAt,
                    HasRecord = dr != null,
                };
            }).ToList();
        }

        public async Task<List<DeclareHallTicketDTO>> GetTableExam(DeclareExamTable dto)
        {
            var all = await GetExam(new GetDeclareExam
            {
                CourseId = dto.CourseId, Ayid = dto.Ayid, Semester = dto.Semester, Pattern = dto.Pattern
            });
            return all.Where(e => e.ExamId == dto.ExamId).ToList();
        }

        public async Task<bool> ToggleReleaseHallTicket(ToggleReleaseHallTicketDTO dto)
        {
            if (dto.ReleaseHallTicket && (dto.HallTicketDeclareDate == null || dto.HallTicketDeclareDate == default(DateTime)))
                throw new ArgumentException("A release date is required while releasing a hall ticket");

            var exam = await DResultService.EligibleExams(_context, ExamPurposes.HallTicket, dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern)
                .FirstOrDefaultAsync(e => e.ExamId == dto.ExamId);
            if (exam == null) return false;

            var existing = (await DResultService.RowsByExam(_context, dto.CourseId, dto.Ayid, dto.Semester, dto.Pattern, new[] { dto.ExamId }))
                .GetValueOrDefault(dto.ExamId);

            if (existing != null)
            {
                existing.ReleaseHallTicket = dto.ReleaseHallTicket;
                existing.HallTicketDeclareDate = dto.ReleaseHallTicket ? dto.HallTicketDeclareDate : null;
                existing.HallTicketUpdatedAt = DateTime.UtcNow;
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
                    ReleaseHallTicket = dto.ReleaseHallTicket,
                    HallTicketDeclareDate = dto.ReleaseHallTicket ? dto.HallTicketDeclareDate : null,
                    HallTicketUpdatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            return true;
        }
    }
}
