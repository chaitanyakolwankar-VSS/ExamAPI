using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.ATKTCummulativeReport
{
    public class ATKTCummulativeReportService: IATKTCummulativeReportService
    {
        public readonly ApplicationDbContext _context;
        public ATKTCummulativeReportService(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task<List<RegularExamResponse>> GetExam(ReportExamRequest dto)
        {
            try
            {
                var exams = _context.Exams.Where(a => a.IsActive == true && ( a.ExamType != "Regular" || a.RevaluationForExamId != null ) && a.CourseId == dto.CourseId && a.AcademicYearAYID == dto.Ayid ).Select(a => new RegularExamResponse
                {
                    ExamId = a.ExamId,
                    Examname = a.RevaluationForExamId != null ? a.Name+' '+a.ExamType + " (Revaluation)" : a.Name+' '+a.ExamType,
                });
                return exams.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task<List<HeadTypeResponse>> GetHeadType(HeadType dto)
        {
            try
            {
                var HeadType = _context.StudentMarks.Join(_context.MarksMasters, sm => sm.MarksId, mm => mm.MarksId, (sm, mm) => new { sm, mm }).Join(_context.SubjectCredits, m => m.sm.CreditsId, sc => sc.CreditsId, (m, sc) => new { m, sc }).Where(x => x.m.mm.ExamId == dto.ExamId && x.m.mm.Pattern == dto.Pattern && x.m.mm.SemesterId == dto.Semester && x.m.mm.AcademicYearAYID==dto.Ayid).Select(a=>new HeadTypeResponse { HeadType=a.sc.HeadType}).Distinct();
                return HeadType.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task<List<AtktReportResponse>> GetReportData(AtktReportRequest dto)
        {
            try
            {
                var result = _context.StudentMarks.Join(_context.MarksMasters, sm => sm.MarksId, mm => mm.MarksId, (sm, mm) => new { sm, mm }).Join(_context.SubjectCredits, x => new { x.sm.CreditsId, x.sm.Head }, sc => new { sc.CreditsId, sc.Head }, (x, sc) => new { x, sc }).Join(_context.SubjectMasters, a => a.x.sm.SubjectId, sbm => sbm.SubjectId, (a, sbm) => new { a, sbm }).Where(w => w.a.x.mm.ExamId == dto.ExamId && w.a.x.mm.SemesterId == dto.Semester && w.a.x.mm.Pattern == dto.Pattern && w.a.x.mm.AcademicYearAYID == dto.Ayid && w.a.sc.HeadType == dto.HeadType && w.a.x.sm.IsCarryForward == false).Select(s => new AtktReportResponse { SubjectName = s.sbm.Name,SeatNo=s.a.x.mm.SeatNo, SubjectCriteria = s.a.sc.HeadType + " : " + s.a.sc.HeadOutOf + "/" + s.a.sc.HeadPass });
                return await result.ToListAsync();
            }
            catch
            {
                throw ;
            }
        }
    }
}
