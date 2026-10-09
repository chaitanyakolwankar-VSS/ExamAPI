using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Lookup;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.AssignSeatNo
{
    public class AssignSeatNoService : IAssignSeatNoService
    {
        public readonly ApplicationDbContext _context;
        private readonly IGenericRepository _genericRepository;
        public AssignSeatNoService(ApplicationDbContext context, IGenericRepository genericRepository)
        {
            _context = context;
            _genericRepository = genericRepository;
        }

        public async Task<List<AssignSeatNoStudents>> GetStudents(GetAssignSeatNoStudents dto)
        {
            try
            {
                // A revaluation exam keeps the seat numbers of the exam it revalues (the picker hides it; checked here too).
                if (await IsRevaluationAsync(dto.ExamId))
                    return new List<AssignSeatNoStudents>();

                var students = _context.MarksMasters.Join(_context.StudentMasters, mm => mm.StdMstId, sm => sm.StdMstId, (mm, sm) => new {mm,sm}).Where(a=>a.mm.SemesterId==dto.Semester && a.mm.ExamId==dto.ExamId && a.mm.AcademicYearAYID==dto.Ayid && a.mm.Pattern == dto.Pattern).Select( s=>new AssignSeatNoStudents { MarksId = s.mm.MarksId, StudentId = s.mm.StudentID, StudentName = s.sm.FirstName + ' ' + s.sm.MiddleName + ' ' + s.sm.LastName,SeatNo=s.mm.SeatNo ??"" ,QuotaType= s.mm.QuotaType ?? "" } ).OrderBy(x => x.StudentId);
                return students.ToList();
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        private Task<bool> IsRevaluationAsync(Guid examId) =>
            _context.Exams.AnyAsync(e => e.ExamId == examId && e.RevaluationForExamId != null);

        public async Task<ApiResponseDto<object>> UpdateSeatNo(SaveSeatNoRequest dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // ✅ Allowed quota list
                var allowedQuotaTypes = new List<string> { "NSS", "NCC", "DLLE", "LD", "SP", "SPORTS" };

                // ✅ Find invalid quota types
                var invalidStudents = dto.Students
                    .Where(x => !string.IsNullOrWhiteSpace(x.QuotaType) &&
                                !allowedQuotaTypes.Contains(x.QuotaType.ToUpper()))
                    .ToList();

                if (invalidStudents.Any())
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "Invalid QuotaType found. Allowed values: NSS, NCC, DLLE, LD, SP (or SPORTS)"
                    };
                }

                var ids = dto.Students.Select(x => x.MarksId).ToList();

                var marksMasters = await _context.MarksMasters
                    .Where(x => ids.Contains(x.MarksId))
                    .ToListAsync();

                var examIds = marksMasters.Select(x => x.ExamId).Distinct().ToList();
                if (await _context.Exams.AnyAsync(e => examIds.Contains(e.ExamId) && e.RevaluationForExamId != null))
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "Seat numbers cannot be changed on a revaluation exam; it keeps the seat numbers of the exam it revalues."
                    };
                }

                foreach (var student in dto.Students)
                {
                    var record = marksMasters.FirstOrDefault(x => x.MarksId == student.MarksId);

                    if (record != null )
                    {
                        record.SeatNo = student.SeatNo;
                    }
                    if (record != null )
                    {
                        record.QuotaType = student.QuotaType;
                    }
                }

                await _context.SaveChangesAsync();

                //foreach (var student in dto.Students.Where(x => x.SeatNo !=""))
                //{
                //    var MarksMasterStudent = await _context.MarksMasters.Where(x => x.MarksId == student.MarksId).FirstOrDefaultAsync();

                //    MarksMasterStudent.SeatNo = student.SeatNo;
                //    _context.MarksMasters.Update(MarksMasterStudent);

                //}
                await transaction.CommitAsync();
                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Students Updated successfully!!"
                };
            }
            catch(Exception ex)
            {
                await transaction.RollbackAsync();
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = "Failed to Update Students!!"
                };
            }
        }
    }
}
