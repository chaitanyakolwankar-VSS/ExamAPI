using System.Security.Claims;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Files;
using ExamAPI.Services.GenerateHallTicket;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.StudentAccess
{
    /// <summary>What the student's hall ticket page shows.</summary>
    public class StudentHallTicketView
    {
        /// <summary>False while the hall ticket can't be shown yet; <see cref="Message"/> says why.</summary>
        public bool Ready { get; set; }
        public string? Message { get; set; }
        /// <summary>"photo" and/or "signature" the student must add before the hall ticket shows.</summary>
        public List<string> Missing { get; set; } = new();
        public string ExamName { get; set; } = "";
        public string ExamType { get; set; } = "";
        public string Branch { get; set; } = "";
        public string Pattern { get; set; } = "";
        public string CollegeName { get; set; } = "";
        public string Center { get; set; } = "";
        public bool HasLogo { get; set; }
        public bool HasPrincipalSign { get; set; }
        public bool HasControllerSign { get; set; }
        public bool HasSeal { get; set; }
        /// <summary>The hall ticket itself (subjects, seat number); null while not <see cref="Ready"/>.</summary>
        public StudentHallTicketData? Student { get; set; }
    }

    public static class HallTicketImageKinds
    {
        public const string Logo = "logo";
        public const string Principal = "principal";
        public const string Controller = "controller";
        public const string Seal = "seal";
    }

    public interface IStudentHallTicketService
    {
        Task<StudentHallTicketView> GetAsync(ClaimsPrincipal student);
        /// <summary>The college's logo, signatures or seal for the hall ticket (kind: <see cref="HallTicketImageKinds"/>).</summary>
        Task<(byte[] Bytes, string ContentType)?> ReadCollegeImageAsync(ClaimsPrincipal student, string kind);
        /// <summary>Records the first download (staff see the link as downloaded).</summary>
        Task MarkDownloadedAsync(ClaimsPrincipal student);
    }

    /// <summary>
    /// A hall ticket link: the student sees the same hall ticket staff print (Generate Hall Ticket), once the exam's
    /// hall ticket is released and their photo and signature are on record. The batch names the exam (RefId) and the
    /// class it was sent for. Reads go through the tenant filter: the session's college is the caller's.
    /// </summary>
    public sealed class StudentHallTicketService : IStudentHallTicketService
    {
        private readonly ApplicationDbContext _context;
        private readonly IGenerateHallTicketService _hallTickets;
        private readonly IFileStorage _storage;

        public StudentHallTicketService(ApplicationDbContext context, IGenerateHallTicketService hallTickets, IFileStorage storage)
        {
            _context = context;
            _hallTickets = hallTickets;
            _storage = storage;
        }

        private async Task<(TaskLink Link, TaskBatch Batch, StudentMaster Student)> LoadAsync(ClaimsPrincipal user)
        {
            var linkId = StudentAccessAuth.Read(user, StudentAccessAuth.LinkClaim) ?? throw new UnauthorizedAccessException();
            var link = await _context.TaskLinks.Include(l => l.Batch).Include(l => l.Student).FirstOrDefaultAsync(l => l.Id == linkId)
                ?? throw new UnauthorizedAccessException();
            if (!StudentAccessAuth.IsOpen(link, DateTime.UtcNow)) throw new UnauthorizedAccessException();
            if (link.TaskTypeCode != TaskTypes.HallTicket || link.Batch?.RefId == null || link.Student == null)
                throw new InvalidOperationException("This link isn't for a hall ticket.");
            return (link, link.Batch, link.Student);
        }

        public async Task<StudentHallTicketView> GetAsync(ClaimsPrincipal user)
        {
            var (_, batch, s) = await LoadAsync(user);
            var exam = await _context.Exams.FirstOrDefaultAsync(e => e.ExamId == batch.RefId)
                ?? throw new InvalidOperationException("This exam is no longer available. Contact your college office.");
            var college = await _hallTickets.HallTicketCollegeData();
            var view = new StudentHallTicketView
            {
                ExamName = exam.Name,
                ExamType = exam.ExamType ?? "",
                Branch = await _context.CourseMasters.Where(c => c.CourseId == batch.CourseId).Select(c => c.Name).FirstOrDefaultAsync() ?? "",
                Pattern = await _context.MarksMasters
                    .Where(m => m.ExamId == exam.ExamId && m.StdMstId == s.StdMstId).Select(m => m.Pattern).FirstOrDefaultAsync() ?? "",
                CollegeName = college.CollegeName,
                Center = college.Center,
                HasLogo = college.Logo != "",
                HasPrincipalSign = college.PrincipalSign != "",
                HasControllerSign = college.ControllerSign != "",
                HasSeal = college.Seal != "",
            };

            var release = batch.CourseId is Guid courseId && batch.AYID is Guid ayid
                ? (await ExamAPI.Services.DeclareResult.DResultService.RowsByExam(_context, courseId, ayid, batch.SemesterId, view.Pattern, new[] { exam.ExamId }))
                    .GetValueOrDefault(exam.ExamId)
                : null;
            if (release is not { ReleaseHallTicket: true })
            {
                view.Message = "Your hall ticket isn't available right now. Contact your college office.";
                return view;
            }
            if (release.HallTicketDeclareDate is DateTime from && from.ToLocalTime().Date > DateTime.Today)
            {
                view.Message = $"Your hall ticket will be available from {from.ToLocalTime():d MMM yyyy}.";
                return view;
            }

            if (string.IsNullOrEmpty(s.PhotoUrl)) view.Missing.Add(StudentImageKinds.Photo);
            if (string.IsNullOrEmpty(s.SignUrl)) view.Missing.Add(StudentImageKinds.Signature);
            if (view.Missing.Count > 0)
            {
                view.Message = "Your hall ticket needs your photo and signature. Add what's missing to see it.";
                return view;
            }

            view.Student = (await _hallTickets.HallTickectStudentData(new StudentHallTicketDataRequest
            {
                Ayid = batch.AYID ?? Guid.Empty,
                ExamId = exam.ExamId,
                Semester = batch.SemesterId ?? "",
                Pattern = view.Pattern,
                CourseId = batch.CourseId ?? Guid.Empty,
                StudentId = s.StudentId,
            })).FirstOrDefault();
            if (view.Student == null)
            {
                view.Message = "Your hall ticket isn't ready yet. Contact your college office.";
                return view;
            }
            view.Ready = true;
            return view;
        }

        public async Task<(byte[] Bytes, string ContentType)?> ReadCollegeImageAsync(ClaimsPrincipal user, string kind)
        {
            await LoadAsync(user);
            var college = await _hallTickets.HallTicketCollegeData();
            var path = kind switch
            {
                HallTicketImageKinds.Logo => college.Logo,
                HallTicketImageKinds.Principal => college.PrincipalSign,
                HallTicketImageKinds.Controller => college.ControllerSign,
                HallTicketImageKinds.Seal => college.Seal,
                _ => null,
            };
            var bytes = await _storage.ReadAllBytesAsync(string.IsNullOrEmpty(path) ? null : path);
            if (bytes == null) return null;
            return (bytes, FileStorage.ImageContentType(path!) ?? "image/png");
        }

        public async Task MarkDownloadedAsync(ClaimsPrincipal user)
        {
            var (link, _, s) = await LoadAsync(user);
            if (link.UsedAt != null) return;
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
                Event = "HallTicketDownloaded",
                Details = $"Hall ticket downloaded by {s.StudentId}.",
            });
            await _context.SaveChangesAsync();
        }
    }
}
