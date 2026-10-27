using System.Globalization;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.AttendenceReport
{
    public class AttendenceReportService:IAttendenceReportService
    {
        public readonly ApplicationDbContext _context;
        public AttendenceReportService(ApplicationDbContext context)
        {
            _context = context;
        }

        // Exams that sit papers: revaluation exams have no attendance.
        public async Task<List<RegularExamResponse>> GetExam(ReportExamRequest dto)
        {
            return await _context.Exams
                .Where(a => a.IsActive == true && a.CourseId == dto.CourseId && a.AcademicYearAYID == dto.Ayid
                            && a.RevaluationForExamId == null)
                .OrderBy(a => a.Name)
                .Select(a => new RegularExamResponse { ExamId = a.ExamId, Examname = a.Name + " " + a.ExamType })
                .ToListAsync();
        }

        /// <summary>
        /// Every timetabled paper of the chosen semester and pattern, in exam order (date, time, then subject order),
        /// with the number of students sitting it. A student sits a paper when they have a seat number and the paper
        /// is among their subjects for the exam: the same rule as their hall ticket.
        /// </summary>
        public async Task<List<AttendenceReportResponseDTO>> GetSubjectStudentCount(
            AttendenceReportRequestDTO request)
        {
            var papers = await (
                from t in _context.TimeTables
                join s in _context.SubjectMasters on t.SubjectId equals s.SubjectId
                where t.ExamId == request.Exam && t.CourseId == request.CourseId
                      && s.SemId == request.Semester && s.Pattern == request.Pattern
                      && t.Date != null && t.Date != "" && t.Time != null && t.Time != ""
                select new { s.SubjectId, s.SubjectCode, s.Name, s.SortOrder, Date = t.Date!, Time = t.Time! })
                .ToListAsync();

            var counts = await Seated(request)
                .GroupBy(x => x.SubjectId)
                .Select(g => new { SubjectId = g.Key, Count = g.Select(x => x.MarksId).Distinct().Count() })
                .ToDictionaryAsync(x => x.SubjectId!.Value, x => x.Count);

            return papers
                .GroupBy(p => p.SubjectId).Select(g => g.First())
                .OrderBy(p => ExamSlotOrder.Date(p.Date))
                .ThenBy(p => ExamSlotOrder.StartTime(p.Time))
                .ThenBy(p => p.Time, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.SortOrder.HasValue ? 0 : 1).ThenBy(p => p.SortOrder ?? int.MaxValue)
                .ThenBy(p => p.SubjectCode, StringComparer.OrdinalIgnoreCase)
                .Select(p => new AttendenceReportResponseDTO
                {
                    SubjectId = p.SubjectId,
                    SubjectCode = p.SubjectCode,
                    SubjectName = p.Name,
                    Date = p.Date,
                    Time = p.Time,
                    StudentCount = counts.GetValueOrDefault(p.SubjectId)
                })
                .ToList();
        }

        /// <summary>Seat numbers of the students sitting one paper, in seat order (blocks are filled in this order).</summary>
        public async Task<List<AttendenceSeatNoResponseDTO>> GetSubjectSeatNo(
       AttendenceSeatNoRequestDTO request)
        {
            var seats = await Seated(request)
                .Where(x => x.SubjectId == request.SubjectId)
                .Select(x => x.SeatNo!)
                .Distinct()
                .ToListAsync();

            return seats
                .OrderBy(s => s.Length).ThenBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select(s => new AttendenceSeatNoResponseDTO { SeatNo = s })
                .ToList();
        }

        // Students of the exam, semester, pattern and year who have a seat number, one row per paper they write: the
        // subject's written head (ESE, or ESA for pharmacy) is re-sat, not carried forward from an earlier exam (ATKT).
        private IQueryable<SeatedRow> Seated(AttendenceReportRequestDTO request)
            => from m in _context.MarksMasters
               join sm in _context.StudentMarks on m.MarksId equals sm.MarksId
               join c in _context.SubjectCredits on new { sm.CreditsId, sm.Head } equals new { c.CreditsId, c.Head }
               where m.ExamId == request.Exam && m.AcademicYearAYID == request.Ayid
                     && m.SemesterId == request.Semester && m.Pattern == request.Pattern
                     && m.SeatNo != null && m.SeatNo != ""
                     && sm.SubjectId != null && !sm.IsCarryForward
                     && c.HeadType != null && (c.HeadType.Contains("ESE") || c.HeadType.Contains("ESA"))
               select new SeatedRow { MarksId = m.MarksId, SeatNo = m.SeatNo, SubjectId = sm.SubjectId };

        private sealed class SeatedRow
        {
            public Guid MarksId { get; set; }
            public string? SeatNo { get; set; }
            public Guid? SubjectId { get; set; }
        }
    }
}
