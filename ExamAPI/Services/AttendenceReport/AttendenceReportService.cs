using ExamAPI.Data;
using ExamAPI.DTOs;

namespace ExamAPI.Services.AttendenceReport
{
    public class AttendenceReportService:IAttendenceReportService
    {
        public readonly ApplicationDbContext _context;
        public AttendenceReportService(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task<List<RegularExamResponse>> GetExam(ReportExamRequest dto)
        {
            try
            {
                var exams = _context.Exams.Where(a => a.IsActive == true  && a.CourseId == dto.CourseId && a.AcademicYearAYID == dto.Ayid).Select(a => new RegularExamResponse
                {
                    ExamId = a.ExamId,
                    Examname = a.RevaluationForExamId != null ? a.Name + ' ' + a.ExamType + " (Revaluation)" : a.Name + ' ' + a.ExamType,
                });
                return exams.ToList();
            }
            catch (Exception ex)
            {
                throw;
            }
        }


        public async Task<List<AttendenceReportResponseDTO>> GetSubjectStudentCount(
            AttendenceReportRequestDTO request)
        {
            var query =
                from t in _context.TimeTables
                join s in _context.SubjectMasters
                    on t.SubjectId equals s.SubjectId
                where t.ExamId == request.Exam
                      && t.CourseId == request.CourseId
                select new AttendenceReportResponseDTO
                {
                    SubjectId=s.SubjectId,
                    SubjectCode = s.SubjectCode,
                    SubjectName = s.Name,
                    Date = t.Date,
                    Time = t.Time,
                    StudentCount = _context.MarksMasters
                        .Where(m =>
                            m.ExamId == t.ExamId &&
                            m.AcademicYearAYID == request.Ayid &&
                            m.Pattern == request.Pattern &&
                            m.SemesterId == request.Semester)
                        .Where(m => _context.StudentMarks.Any(sm =>
                            sm.MarksId == m.MarksId &&
                            sm.SubjectId == s.SubjectId))
                        .Select(m => m.StudentID)
                        .Distinct()
                        .Count()
                };

            return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(
                    query.OrderBy(x => x.Date)
                         .ThenBy(x => x.Time)
                         .ThenBy(x => x.SubjectCode));
        }
        public async Task<List<AttendenceSeatNoResponseDTO>> GetSubjectSeatNo(
       AttendenceSeatNoRequestDTO request)
        {
            var query =  _context.MarksMasters
                .Join(
                    _context.StudentMarks,
                    m => m.MarksId,
                    s => s.MarksId,
                    (m, s) => new { m, s }
                )
                .Where(w =>
                    w.m.ExamId == request.Exam &&
                    w.m.SemesterId == request.Semester &&
                    w.m.AcademicYearAYID == request.Ayid &&
                    w.m.Pattern == request.Pattern &&
                    w.s.SubjectId == request.SubjectId
                )
                .Select(w => new AttendenceSeatNoResponseDTO
                {
                    SeatNo = w.m.SeatNo
                })
                .Distinct()
                .ToList();

            return query;
        }
    }
}
