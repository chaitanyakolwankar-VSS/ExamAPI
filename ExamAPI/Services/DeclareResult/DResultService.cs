using Azure.Messaging;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Common;
using Microsoft.AspNetCore.Http.HttpResults;
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

        public async Task<List<DeclareResultDTO>> GetExam(GetDeclareExam dto)
        {
            var exams = from em in _context.Exams
                        join dr in _context.DeclareResults on em.ExamId equals dr.ExamId
                        where em.IsActive == true
                              && !em.IsDeleted
                              && !em.IsLocked
                              // ExamMaster.Semester is never written (Exam Master has no semester): filter on the DeclareResult row.
                              && !dr.IsDeleted
                              && dr.AcademicYear == em.AcademicYearAYID
                              && em.AcademicYearAYID == dto.Ayid
                              && dr.Sem_id == dto.Semester
                              && em.CourseId == dto.CourseId
                              && dr.Pattern == dto.Pattern
                        select new DeclareResultDTO
                        {
                            ExamId = em.ExamId,
                            CourseId = em.CourseId ?? Guid.Empty,
                            Ayid = em.AcademicYearAYID ?? Guid.Empty,
                            Semester = dto.Semester,
                            Pattern = dto.Pattern,
                            Examname = em.RevaluationForExamId != null
                                ? em.Name + " (Revaluation)"
                                : em.ExamType == "A.T.K.T"
                                    ? em.Name + " (A.T.K.T)"
                                    : em.Name
                        };

            return await exams.ToListAsync();
        }

        public async Task<List<DeclareResultDTO>> GetTableExam(DeclareExamTable dto)
        {
            var exams = from e in _context.Exams
                        join dr in _context.DeclareResults on e.ExamId equals dr.ExamId
                        where e.CourseId == dto.CourseId && dr.Sem_id == dto.Semester && e.AcademicYearAYID == dto.Ayid && e.ExamId==dto.ExamId && !e.IsDeleted && !dr.IsDeleted && dr.Pattern==dto.Pattern && dr.ResDeclare>0
                        select new DeclareResultDTO
                        {
                            ExamId = e.ExamId,
                            CourseId = e.CourseId ?? Guid.Empty,
                            Ayid = e.AcademicYearAYID ?? Guid.Empty,
                            Semester = dto.Semester,
                            DeclareDate=dr.DeclareDate,
                            IsDeclare=dr.IsDeclare,
                            Pattern=dto.Pattern,
                            Examname = e.RevaluationForExamId != null ? e.Name + "(Revaluation)" : e.ExamType == "A.T.K.T" ? e.Name + "(A.T.K.T)" : e.Name
                        };

            return await exams.ToListAsync();
        }


        public async Task<bool> ToggleDeclare (ToggleDeclareResultDTO dto)
        {
            if (dto.IsDeclare && dto.DeclareDate == default)
                throw new ArgumentException("Declare Date is required while declaring a result");

            var existing = await _context.DeclareResults
                .FirstOrDefaultAsync(dr => dr.ExamId == dto.ExamId && dr.CourseId == dto.CourseId && dr.AcademicYear == dto.Ayid && dr.Sem_id == dto.Semester && !dr.IsDeleted && dr.Pattern==dto.Pattern);

            if (existing != null)
            {
                existing.IsDeclare = dto.IsDeclare;
                existing.DeclareDate = dto.IsDeclare ? dto.DeclareDate : (DateTime?)null; 
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
                    IsDeclare = dto.IsDeclare,
                    DeclareDate = dto.DeclareDate
                };
                _context.DeclareResults.Add(newRecord);
            }

            await _context.SaveChangesAsync();
            return true;
        }
    }
}
