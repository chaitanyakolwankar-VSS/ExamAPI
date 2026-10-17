using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.StudentAccess
{
    /// <summary>A student as the email review lists them.</summary>
    public class StudentEmailRow
    {
        public string StudentId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Branch { get; set; } = "";
        public string? SemesterId { get; set; }
        public string? Email { get; set; }
        /// <summary><see cref="StudentEmailStates"/>.</summary>
        public string Status { get; set; } = "";
        public string? Reason { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>What the review shows for a student's email.</summary>
    public static class StudentEmailStates
    {
        public const string Missing = "Missing";
        public const string Flagged = "Flagged";
        /// <summary>An email nobody has confirmed yet (not flagged either).</summary>
        public const string Unchecked = "Unchecked";
        public const string Verified = "Verified";
    }

    public class StudentEmailList
    {
        public int Missing { get; set; }
        public int Flagged { get; set; }
        public int Unchecked { get; set; }
        public int Verified { get; set; }
        public List<StudentEmailRow> Rows { get; set; } = new();
    }

    public class StudentEmailFilter
    {
        public Guid AYID { get; set; }
        public Guid? CourseId { get; set; }
        public string? SemesterId { get; set; }
        /// <summary>"attention" (missing + flagged, the default), "unchecked", "verified" or "all".</summary>
        public string? Show { get; set; }
    }

    public class StudentEmailUpdate
    {
        public string? Email { get; set; }
        /// <summary>Also mark the email as checked by staff (overrides a domain flag).</summary>
        public bool Verify { get; set; }
    }

    public class EmailSettingsDto
    {
        /// <summary>Comma-separated; empty = any domain.</summary>
        public string? AllowedEmailDomains { get; set; }
        public string? ExamOfficeEmail { get; set; }
        public int DailyEmailLimit { get; set; } = 2000;
    }

    public interface IStudentEmailReviewService
    {
        Task<StudentEmailList> ListAsync(StudentEmailFilter filter);
        Task<StudentEmailRow> UpdateAsync(string studentId, StudentEmailUpdate update);
        Task<int> VerifyAsync(IReadOnlyCollection<string> studentIds);
        Task<EmailSettingsDto> GetSettingsAsync();
        /// <summary>Saves the settings and re-checks every email staff have not verified; answers how many are flagged now.</summary>
        Task<int> SaveSettingsAsync(EmailSettingsDto dto);
    }

    /// <summary>
    /// Staff review of student emails before links are sent: lists missing / flagged / unchecked ones of a year
    /// (optionally a branch and semester), lets staff correct an email and mark it Verified, and holds the
    /// college's email settings. Every change is written to the audit log.
    /// </summary>
    public class StudentEmailReviewService : IStudentEmailReviewService
    {
        private readonly ApplicationDbContext _context;

        public StudentEmailReviewService(ApplicationDbContext context) => _context = context;

        private static string StateOf(StudentMaster s) =>
            s.Email == null ? StudentEmailStates.Missing
            : s.EmailStatus == EmailVerification.Verified ? StudentEmailStates.Verified
            : s.EmailStatus == EmailVerification.Flagged ? StudentEmailStates.Flagged
            : StudentEmailStates.Unchecked;

        public async Task<StudentEmailList> ListAsync(StudentEmailFilter filter)
        {
            var query = from se in _context.StudentEligibilities
                        join sm in _context.StudentMasters on se.StudentId equals sm.StudentId
                        join cm in _context.CourseMasters on se.CourseId equals cm.CourseId
                        where se.AYID == filter.AYID
                        select new { sm, Branch = cm.Name, se.SemesterId, se.CourseId };
            if (filter.CourseId is Guid course && course != Guid.Empty)
                query = query.Where(x => x.CourseId == course);
            if (!string.IsNullOrWhiteSpace(filter.SemesterId))
                query = query.Where(x => x.SemesterId == filter.SemesterId);

            var all = (await query.ToListAsync())
                .GroupBy(x => x.sm.StdMstId).Select(g => g.First()) // one row per student
                .Select(x => new StudentEmailRow
                {
                    StudentId = x.sm.StudentId,
                    Name = StudentNames.Display(x.sm),
                    Branch = x.Branch,
                    SemesterId = x.SemesterId,
                    Email = x.sm.Email,
                    Status = StateOf(x.sm),
                    Reason = x.sm.Email == null ? "No email yet." : x.sm.EmailStatus == EmailVerification.Flagged ? x.sm.EmailFlagReason : null,
                    IsActive = x.sm.IsActive,
                })
                .ToList();

            var shown = (filter.Show ?? "attention").ToLowerInvariant() switch
            {
                "all" => all,
                "unchecked" => all.Where(r => r.Status == StudentEmailStates.Unchecked),
                "verified" => all.Where(r => r.Status == StudentEmailStates.Verified),
                _ => all.Where(r => r.Status is StudentEmailStates.Missing or StudentEmailStates.Flagged),
            };

            return new StudentEmailList
            {
                Missing = all.Count(r => r.Status == StudentEmailStates.Missing),
                Flagged = all.Count(r => r.Status == StudentEmailStates.Flagged),
                Unchecked = all.Count(r => r.Status == StudentEmailStates.Unchecked),
                Verified = all.Count(r => r.Status == StudentEmailStates.Verified),
                Rows = shown.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.StudentId, StringComparer.Ordinal).ToList(),
            };
        }

        public async Task<StudentEmailRow> UpdateAsync(string studentId, StudentEmailUpdate update)
        {
            var student = await _context.StudentMasters.FirstOrDefaultAsync(s => s.StudentId == studentId)
                ?? throw new KeyNotFoundException("Student not found.");

            var email = StudentEmail.Normalize(update.Email);
            if (email == "")
                throw new InvalidOperationException("Enter the student's email.");
            if (!StudentEmail.IsValid(email))
                throw new InvalidOperationException($"\"{email}\" is not a valid email address.");
            var owners = await StudentEmail.OwnersAsync(_context, new[] { email });
            if (owners.TryGetValue(email, out var owner) && owner != student.StudentId)
                throw new InvalidOperationException($"Email {email} already belongs to student {owner}.");

            if (email != student.Email)
            {
                var old = student.Email;
                StudentEmail.Apply(student, email, await StudentEmail.AllowedDomainsAsync(_context));
                Audit(student, "EmailSet", $"Student {student.StudentId}: email {old ?? "(none)"} -> {email} on the email review.");
            }
            if (update.Verify && student.EmailStatus != EmailVerification.Verified)
                MarkVerified(student);

            await _context.SaveChangesAsync();
            var course = await (from se in _context.StudentEligibilities
                                join cm in _context.CourseMasters on se.CourseId equals cm.CourseId
                                where se.StudentId == student.StudentId
                                orderby se.CreatedAt descending
                                select new { cm.Name, se.SemesterId }).FirstOrDefaultAsync();
            return new StudentEmailRow
            {
                StudentId = student.StudentId,
                Name = StudentNames.Display(student),
                Branch = course?.Name ?? "",
                SemesterId = course?.SemesterId,
                Email = student.Email,
                Status = StateOf(student),
                Reason = student.EmailStatus == EmailVerification.Flagged ? student.EmailFlagReason : null,
                IsActive = student.IsActive,
            };
        }

        public async Task<int> VerifyAsync(IReadOnlyCollection<string> studentIds)
        {
            var ids = studentIds.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToList();
            if (ids.Count == 0) return 0;
            // Only students with an email; one already verified is left alone.
            var students = await _context.StudentMasters
                .Where(s => ids.Contains(s.StudentId) && s.Email != null && s.EmailStatus != EmailVerification.Verified)
                .ToListAsync();
            foreach (var student in students) MarkVerified(student);
            await _context.SaveChangesAsync();
            return students.Count;
        }

        private void MarkVerified(StudentMaster student)
        {
            var flag = student.EmailStatus == EmailVerification.Flagged ? $" (was flagged: {student.EmailFlagReason})" : "";
            student.EmailStatus = EmailVerification.Verified;
            student.EmailFlagReason = null;
            Audit(student, "EmailVerified", $"Student {student.StudentId}: email {student.Email} marked verified{flag}.");
        }

        private void Audit(StudentMaster student, string evt, string details) =>
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                Action = "UPDATE",
                TableName = nameof(StudentMaster),
                RecordId = student.StdMstId.ToString(),
                PerformedBy = _context.CurrentUserId,
                UserType = "Staff",
                Event = evt,
                Details = details.Length > 1000 ? details[..1000] : details,
            });

        public async Task<EmailSettingsDto> GetSettingsAsync()
        {
            var s = await _context.CollegeSettings.FirstOrDefaultAsync();
            return new EmailSettingsDto
            {
                AllowedEmailDomains = s?.AllowedEmailDomains,
                ExamOfficeEmail = s?.ExamOfficeEmail,
                DailyEmailLimit = s?.DailyEmailLimit ?? 2000,
            };
        }

        public async Task<int> SaveSettingsAsync(EmailSettingsDto dto)
        {
            if (_context.CurrentCollegeId == null)
                throw new InvalidOperationException("Email settings belong to a college: sign in as that college's admin.");
            var domains = StudentEmail.ParseDomains(dto.AllowedEmailDomains);
            if (domains.Any(d => !StudentEmail.IsValid("a@" + d)))
                throw new InvalidOperationException("Enter the allowed domains like vivatech.in, separated by commas.");
            var office = StudentEmail.Normalize(dto.ExamOfficeEmail);
            if (office != "" && !StudentEmail.IsValid(office))
                throw new InvalidOperationException("Enter a valid exam office email.");
            if (dto.DailyEmailLimit < 1 || dto.DailyEmailLimit > 100000)
                throw new InvalidOperationException("The daily email limit must be between 1 and 100000.");

            var settings = await _context.CollegeSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new CollegeSettings { Id = Guid.NewGuid() };
                _context.CollegeSettings.Add(settings);
            }
            var joined = string.Join(", ", domains);
            settings.AllowedEmailDomains = joined == "" ? null : joined;
            settings.ExamOfficeEmail = office == "" ? null : office;
            settings.DailyEmailLimit = dto.DailyEmailLimit;

            // Re-check every email staff have not verified against the new list (verified ones stay verified).
            var students = await _context.StudentMasters.Where(s => s.Email != null && s.EmailStatus != EmailVerification.Verified).ToListAsync();
            foreach (var student in students)
            {
                var flag = StudentEmail.FlagReason(student.Email!, domains);
                var status = flag == null ? null : EmailVerification.Flagged;
                if (student.EmailStatus != status || student.EmailFlagReason != flag)
                {
                    student.EmailStatus = status;
                    student.EmailFlagReason = flag;
                }
            }

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                Action = "UPDATE",
                TableName = nameof(CollegeSettings),
                RecordId = settings.Id.ToString(),
                PerformedBy = _context.CurrentUserId,
                UserType = "Staff",
                Event = "EmailSettingsChanged",
                Details = $"Allowed domains: {(joined == "" ? "any" : joined)}; exam office: {(office == "" ? "(none)" : office)}; daily limit: {dto.DailyEmailLimit}.",
            });
            await _context.SaveChangesAsync();
            return students.Count(s => s.EmailStatus == EmailVerification.Flagged);
        }
    }
}
