using System.Security.Claims;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.DeclareResult;
using ExamAPI.Services.Report;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.StudentAccess
{
    /// <summary>What the student's marksheet page shows.</summary>
    public class StudentMarksheetView
    {
        /// <summary>False while the result is not declared (or was withdrawn); <see cref="Message"/> says why.</summary>
        public bool Ready { get; set; }
        public string? Message { get; set; }
        public string ExamName { get; set; } = "";
        public string Semester { get; set; } = "";
        public string CollegeName { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string StudentId { get; set; } = "";
        public DateTime? ResultDate { get; set; }
    }

    public interface IStudentMarksheetService
    {
        Task<StudentMarksheetView> GetAsync(ClaimsPrincipal student);

        /// <summary>The marksheet PDF; records the first download on the link.</summary>
        Task<(byte[] Bytes, string FileName)> PdfAsync(ClaimsPrincipal student);
    }

    /// <summary>
    /// A marksheet link: the student sees the same marksheet staff generate, once the result is declared. The batch
    /// names the exam (RefId) and the class it was sent for. Everything is read from the signed-in link and the
    /// student's own marks row; the browser sends nothing but the session header. Reads go through the tenant filter:
    /// the session's college is the caller's.
    /// </summary>
    public sealed class StudentMarksheetService : IStudentMarksheetService
    {
        private readonly ApplicationDbContext _context;
        private readonly IReportService _reports;

        public StudentMarksheetService(ApplicationDbContext context, IReportService reports)
        {
            _context = context;
            _reports = reports;
        }

        private async Task<(TaskLink Link, TaskBatch Batch, StudentMaster Student)> LoadAsync(ClaimsPrincipal user)
        {
            var linkId = StudentAccessAuth.Read(user, StudentAccessAuth.LinkClaim) ?? throw new UnauthorizedAccessException();
            var link = await _context.TaskLinks.Include(l => l.Batch).Include(l => l.Student).FirstOrDefaultAsync(l => l.Id == linkId)
                ?? throw new UnauthorizedAccessException();
            if (!StudentAccessAuth.IsOpen(link, DateTime.UtcNow)) throw new UnauthorizedAccessException();
            if (link.TaskTypeCode != TaskTypes.Marksheet || link.Batch?.RefId == null || link.Student == null)
                throw new InvalidOperationException("This link isn't for a marksheet.");
            return (link, link.Batch, link.Student);
        }

        /// <summary>The exam, the student's pattern and the Declare Result row (all read, never stored).</summary>
        private async Task<(ExamMaster Exam, string Pattern, ExamAPI.Models.DeclareResult? Declare)> LoadExamAsync(
            TaskBatch batch, Guid studentId)
        {
            var exam = await _context.Exams.FirstOrDefaultAsync(e => e.ExamId == batch.RefId)
                ?? throw new InvalidOperationException("This exam is no longer available. Contact your college office.");

            // Same source as the hall ticket: the student's own marks row for this exam and semester. The PDF uses
            // exactly this pattern and semester, so finding it here means the PDF can find it too.
            var pattern = await _context.MarksMasters
                .Where(m => !m.IsDeleted && m.ExamId == exam.ExamId && m.StdMstId == studentId
                            && m.SemesterId == batch.SemesterId)
                .Select(m => m.Pattern)
                .FirstOrDefaultAsync() ?? "";

            ExamAPI.Models.DeclareResult? declare = null;
            if (batch.CourseId is Guid courseId && batch.AYID is Guid ayid && !string.IsNullOrWhiteSpace(pattern))
            {
                declare = (await DResultService.RowsByExam(
                        _context, courseId, ayid, batch.SemesterId, pattern, new[] { exam.ExamId }))
                    .GetValueOrDefault(exam.ExamId);
            }
            return (exam, pattern, declare);
        }

        public async Task<StudentMarksheetView> GetAsync(ClaimsPrincipal user)
        {
            var (link, batch, s) = await LoadAsync(user);
            var (exam, pattern, declare) = await LoadExamAsync(batch, s.StdMstId);
            var declared = declare?.IsDeclare == true;
            var hasMarks = !string.IsNullOrWhiteSpace(pattern);

            return new StudentMarksheetView
            {
                Ready = declared && hasMarks,
                Message = !hasMarks
                    ? "Your marksheet isn't ready yet. Contact your college office."
                    : declared
                        ? null
                        : "Your result hasn't been declared yet. Check back after your college declares it.",
                ExamName = exam.Name,
                Semester = batch.SemesterId ?? "",
                CollegeName = await StudentLinkEmails.CollegeNameAsync(_context, link.CollegeId),
                StudentName = StudentNames.Display(s),
                StudentId = s.StudentId,
                ResultDate = declare?.DeclareDate,
            };
        }

        public async Task<(byte[] Bytes, string FileName)> PdfAsync(ClaimsPrincipal user)
        {
            var (link, batch, s) = await LoadAsync(user);
            var (exam, pattern, declare) = await LoadExamAsync(batch, s.StdMstId);

            if (declare?.IsDeclare != true)
                throw new InvalidOperationException("Your result hasn't been declared yet.");
            if (string.IsNullOrWhiteSpace(pattern))
                throw new InvalidOperationException("Your marksheet isn't ready yet. Contact your college office.");

            byte[] bytes;
            try
            {
                // The same single-student PDF the staff Generate Result produces. The emailed copy is the current
                // marksheet: current semester, no history, remark as stored.
                bytes = await _reports.GenerateMarksheetPdfAsync(
                    s.StdMstId,
                    exam.ExamId,
                    batch.SemesterId ?? "",
                    pattern,
                    includeHistory: false,
                    resultDate: declare.DeclareDate,
                    collegeId: link.CollegeId ?? Guid.Empty,
                    noRleForFail: false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new InvalidOperationException("Your marksheet could not be made. Contact your college office.", ex);
            }

            if (link.UsedAt == null)
            {
                link.UsedAt = DateTime.UtcNow;
                _context.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    CollegeId = link.CollegeId,
                    Action = "UPDATE",
                    TableName = nameof(TaskLink),
                    RecordId = link.Id.ToString(),
                    PerformedBy = s.StdMstId,
                    UserType = StudentAccessAuth.RoleName,
                    Event = "MarksheetDownloaded",
                    Details = $"Marksheet downloaded by {s.StudentId} for {exam.Name}.",
                });
                await _context.SaveChangesAsync();
            }

            var name = $"Marksheet {s.StudentId} {exam.Name}.pdf";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
            return (bytes, name);
        }
    }
}
