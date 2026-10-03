using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Common;
using ExamAPI.Services.DeclareResult;
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

        public async Task<List<DeclareHallTicketDTO>> GetTableExam(DeclareExamTable dto)
        {
            var exams = from em in _context.Exams
                        join dr in _context.DeclareResults on em.ExamId equals dr.ExamId
                        join ay in _context.AcademicYears on dr.AcademicYear equals ay.AYID
                        where em.AcademicYearAYID == dr.AcademicYear
                              // ExamMaster.Semester is never written (Exam Master has no semester): the DeclareResult row carries it.
                              && !ay.IsDeleted
                              && !dr.IsDeleted
                              && !em.IsDeleted
                              && em.ExamId == dto.ExamId
                              && dr.Sem_id == dto.Semester
                              && dr.AcademicYear == dto.Ayid
                              && dr.Pattern == dto.Pattern
                              && em.IsActive == true
                        select new DeclareHallTicketDTO
                        {
                            ExamId = em.ExamId,
                            Examname = em.Name,
                            CourseId = em.CourseId ?? Guid.Empty,
                            Ayid = em.AcademicYearAYID ?? Guid.Empty,
                            HallTicketDeclareDate=dr.HallTicketDeclareDate,
                            Semester = dr.Sem_id,
                            ReleaseHallTicket = dr.ReleaseHallTicket,
                            Pattern = dr.Pattern,
                        };
            return await exams.ToListAsync();
        }

        public async Task<bool> ToggleReleaseHallTicket(ToggleReleaseHallTicketDTO dto)
        {
            if (dto.ReleaseHallTicket && dto.HallTicketDeclareDate == default)
                throw new ArgumentException("Declare Date is required while declaring a result");

            var existing = await _context.DeclareResults
                .FirstOrDefaultAsync(dr => dr.ExamId == dto.ExamId && dr.CourseId == dto.CourseId && dr.AcademicYear == dto.Ayid && dr.Sem_id == dto.Semester && !dr.IsDeleted && dr.Pattern == dto.Pattern);

            if (existing != null)
            {
                existing.ReleaseHallTicket = dto.ReleaseHallTicket;
                existing.HallTicketDeclareDate = dto.ReleaseHallTicket ? dto.HallTicketDeclareDate : (DateTime?)null;
                existing.HallTicketUpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var newRecord = new ExamAPI.Models.DeclareResult
                {
                    DeclareID = Guid.NewGuid(),
                    ExamId = dto.ExamId,
                    CourseId = dto.CourseId,
                    AcademicYear = dto.Ayid,
                    Sem_id = dto.Semester,
                    Pattern = dto.Pattern,
                    ReleaseHallTicket = dto.ReleaseHallTicket,
                    HallTicketDeclareDate = dto.HallTicketDeclareDate
                };
                _context.DeclareResults.Add(newRecord);
            }

            await _context.SaveChangesAsync();
            return true;
        }
    }
}
