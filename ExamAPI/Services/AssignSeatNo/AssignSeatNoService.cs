using ExamAPI.Data;
using ExamAPI.DTOs;
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

        public async Task<List<ExamResponse>> GetExam(GetAssignSeatNoExam dto)
        {
            try
            {
                var exams =_context.MarksMasters.Where(a=>a.SemesterId==dto.Semester && a.AcademicYearAYID==dto.Ayid).Select(x=>x.ExamId).Distinct().ToList();
                var AssignSeatNoExam = _context.Exams.Where(a => a.IsActive == true  && a.RevaluationForExamId == null && a.CourseId == dto.Courseid && a.AcademicYearAYID == dto.Ayid &&  exams.Contains(a.ExamId)).Select(a => new ExamResponse
                {
                    ExamId = a.ExamId,
                    Examname = a.RevaluationForExamId != null ? a.Name + " (Revaluation)" : a.Name,
                });
                return AssignSeatNoExam.ToList();
            }
            catch(Exception ex) {
                throw new Exception(ex.Message);
            }
        }

        public async Task<List<AssignSeatNoStudents>> GetStudents(GetAssignSeatNoStudents dto)
        {
            try
            {
                var students = _context.MarksMasters.Join(_context.StudentMasters, mm => mm.StdMstId, sm => sm.StdMstId, (mm, sm) => new {mm,sm}).Where(a=>a.mm.SemesterId==dto.Semester && a.mm.ExamId==dto.ExamId && a.mm.AcademicYearAYID==dto.Ayid && a.mm.Pattern == dto.Pattern).Select( s=>new AssignSeatNoStudents { MarksId = s.mm.MarksId, StudentId = s.mm.StudentID, StudentName = s.sm.FirstName + ' ' + s.sm.MiddleName + ' ' + s.sm.LastName,SeatNo=s.mm.SeatNo ??"" ,QuotaType= s.mm.QuotaType ?? "" } ).OrderBy(x => x.StudentId);
                return students.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task<ApiResponseDto<object>> UpdateSeatNo(SaveSeatNoRequest dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // ✅ Allowed quota list
                var allowedQuotaTypes = new List<string> { "NSS", "NCC", "DLLE", "LD", "SP" };

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
                        Message = "Invalid QuotaType found. Allowed values: NSS, NCC, DLLE, LD, SP"
                    };
                }

                var ids = dto.Students.Select(x => x.MarksId).ToList();

                var marksMasters = await _context.MarksMasters
                    .Where(x => ids.Contains(x.MarksId))
                    .ToListAsync();

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
