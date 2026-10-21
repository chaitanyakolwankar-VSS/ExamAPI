using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using ExamAPI.Services.Files;
using ExamAPI.Services.StudentMasters;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.StudentAccess
{
    /// <summary>What the student sends from the details form. Only the fields the batch lets them change are read.</summary>
    public class StudentDetailsUpdate
    {
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? MotherName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Category { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Prn { get; set; }
    }

    public class StudentImageUpload
    {
        /// <summary>A data URL or plain base64 (the page shrinks phone photos before sending).</summary>
        public string? Image { get; set; }
    }

    public class StudentSaveResult
    {
        /// <summary>Labels of the fields that changed ("Phone", "Photo"); empty = nothing was different.</summary>
        public List<string> Changed { get; set; } = new();
        public StudentHome Home { get; set; } = new();
    }

    public static class StudentImageKinds
    {
        public const string Photo = "photo";
        public const string Signature = "signature";
    }

    public interface IStudentDetailsService
    {
        Task<StudentSaveResult> SaveAsync(ClaimsPrincipal student, StudentDetailsUpdate update);
        Task<StudentSaveResult> SaveImageAsync(ClaimsPrincipal student, string kind, string? image);
        Task<(byte[] Bytes, string ContentType)?> ReadImageAsync(ClaimsPrincipal student, string kind);
    }

    /// <summary>
    /// The student changes their own Student Master record through their link: only the fields the batch allows
    /// (others are ignored), checked like the staff form, saved straight away (no approval). Each save is audited
    /// with old and new values, marks the link Updated, and emails the student a notice (at most one per 10 minutes)
    /// so a change they did not make does not go unnoticed. Reads and writes go through the tenant filter: the
    /// session's college is the caller's.
    /// </summary>
    public sealed partial class StudentDetailsService : IStudentDetailsService
    {
        public const int MaxImageBytes = 2 * 1024 * 1024;
        public static readonly TimeSpan NoticeGap = TimeSpan.FromMinutes(10);
        public const string ChangedEvent = "StudentDetailsChanged";

        private readonly ApplicationDbContext _context;
        private readonly IStudentSignInService _signIn;
        private readonly IEmailOutbox _outbox;
        private readonly IFileStorage _storage;

        public StudentDetailsService(ApplicationDbContext context, IStudentSignInService signIn, IEmailOutbox outbox, IFileStorage storage)
        {
            _context = context;
            _signIn = signIn;
            _outbox = outbox;
            _storage = storage;
        }

        public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
        {
            [StudentFields.FirstName] = "First name",
            [StudentFields.MiddleName] = "Middle name",
            [StudentFields.LastName] = "Last name",
            [StudentFields.MotherName] = "Mother's name",
            [StudentFields.DateOfBirth] = "Date of birth",
            [StudentFields.Gender] = "Gender",
            [StudentFields.Category] = "Category",
            [StudentFields.Phone] = "Phone",
            [StudentFields.Address] = "Address",
            [StudentFields.Prn] = "ERN / PRN",
            [StudentFields.Photo] = "Photo",
            [StudentFields.Signature] = "Signature",
        };

        [GeneratedRegex(@"^[\p{L}][\p{L} .'\-]*$")]
        private static partial Regex NamePattern();

        [GeneratedRegex(@"^[A-Za-z0-9/\-]+$")]
        private static partial Regex PrnPattern();

        private async Task<(TaskLink Link, StudentMaster Student, HashSet<string> Editable)> LoadAsync(ClaimsPrincipal user)
        {
            var linkId = StudentAccessAuth.Read(user, StudentAccessAuth.LinkClaim) ?? throw new UnauthorizedAccessException();
            var link = await _context.TaskLinks.Include(l => l.Batch).FirstOrDefaultAsync(l => l.Id == linkId)
                ?? throw new UnauthorizedAccessException();
            if (!StudentAccessAuth.IsOpen(link, DateTime.UtcNow)) throw new UnauthorizedAccessException();
            var student = await _context.StudentMasters.FirstOrDefaultAsync(s => s.StdMstId == link.StdMstId)
                ?? throw new UnauthorizedAccessException();
            return (link, student, new HashSet<string>(StudentFields.Parse(link.Batch?.EditableFields)));
        }

        private static string? Clean(string? s)
        {
            var t = string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return t.Length == 0 ? null : t;
        }

        private static string? Name(string? value, string label, bool required)
        {
            var v = Clean(value);
            if (v == null)
                return required ? throw new InvalidOperationException($"Enter your {label.ToLowerInvariant()}.") : null;
            if (v.Length > 50 || !NamePattern().IsMatch(v))
                throw new InvalidOperationException($"{label}: use letters only (spaces, dots and hyphens are fine), at most 50.");
            return v;
        }

        /// <summary>10 digits starting 6-9 (Indian mobile); "+91", "0", spaces and dashes are accepted and dropped.</summary>
        public static string? Phone(string? value)
        {
            var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return null;
            if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
            else if (digits.Length == 11 && digits.StartsWith('0')) digits = digits[1..];
            if (digits.Length != 10 || digits[0] < '6')
                throw new InvalidOperationException("Phone: enter a 10-digit mobile number.");
            return digits;
        }

        public async Task<StudentSaveResult> SaveAsync(ClaimsPrincipal user, StudentDetailsUpdate update)
        {
            var (link, s, editable) = await LoadAsync(user);
            var changes = new List<(string Field, string? Old, string? New)>();

            void Set(string field, string? oldValue, string? newValue, Action<string?> apply)
            {
                if (!editable.Contains(field) || string.Equals(oldValue ?? "", newValue ?? "", StringComparison.Ordinal)) return;
                changes.Add((field, oldValue, newValue));
                apply(newValue);
            }

            if (editable.Contains(StudentFields.FirstName))
                Set(StudentFields.FirstName, s.FirstName, Name(update.FirstName, "First name", true), v => s.FirstName = v!);
            if (editable.Contains(StudentFields.MiddleName))
                Set(StudentFields.MiddleName, s.MiddleName, Name(update.MiddleName, "Middle name", false), v => s.MiddleName = v);
            if (editable.Contains(StudentFields.LastName))
                Set(StudentFields.LastName, s.LastName, Name(update.LastName, "Last name", true), v => s.LastName = v!);
            if (editable.Contains(StudentFields.MotherName))
                Set(StudentFields.MotherName, s.MotherName, Name(update.MotherName, "Mother's name", true), v => s.MotherName = v);
            if (editable.Contains(StudentFields.DateOfBirth))
            {
                DateTime? dob = update.DateOfBirth?.Date;
                if (dob is DateTime d && (d > DateTime.Today.AddYears(-14) || d < DateTime.Today.AddYears(-80)))
                    throw new InvalidOperationException("Date of birth: check the year.");
                var oldDob = s.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var newDob = dob?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Set(StudentFields.DateOfBirth, oldDob, newDob, _ => s.DateOfBirth = dob);
            }
            if (editable.Contains(StudentFields.Gender))
            {
                var g = Clean(update.Gender);
                g = string.Equals(g, "Male", StringComparison.OrdinalIgnoreCase) ? "Male"
                    : string.Equals(g, "Female", StringComparison.OrdinalIgnoreCase) ? "Female"
                    : throw new InvalidOperationException("Choose your gender.");
                Set(StudentFields.Gender, s.Gender, g, v => s.Gender = v);
            }
            if (editable.Contains(StudentFields.Category))
            {
                var c = StudentMasterService.StudentCategories.FirstOrDefault(x => string.Equals(x, Clean(update.Category), StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Choose your category.");
                Set(StudentFields.Category, s.Category, c, v => s.Category = v);
            }
            if (editable.Contains(StudentFields.Phone))
                Set(StudentFields.Phone, s.Phone, Phone(update.Phone), v => s.Phone = v);
            if (editable.Contains(StudentFields.Address))
            {
                var a = Clean(update.Address);
                if (a?.Length > 300) throw new InvalidOperationException("Address: at most 300 characters.");
                Set(StudentFields.Address, s.Address, a, v => s.Address = v);
            }
            if (editable.Contains(StudentFields.Prn))
            {
                var p = Clean(update.Prn)?.ToUpperInvariant();
                if (p != null && (p.Length > 30 || !PrnPattern().IsMatch(p)))
                    throw new InvalidOperationException("ERN / PRN: letters, digits, '-' and '/' only.");
                if (p != null && p != s.StudentPRN
                    && await _context.StudentMasters.AnyAsync(x => x.StudentPRN == p && x.StdMstId != s.StdMstId && !x.IsDeleted))
                    throw new InvalidOperationException("This ERN / PRN belongs to another student. Check it, or contact your college office.");
                Set(StudentFields.Prn, s.StudentPRN, p, v => s.StudentPRN = v);
            }

            return await FinishAsync(user, link, s, changes);
        }

        public async Task<StudentSaveResult> SaveImageAsync(ClaimsPrincipal user, string kind, string? image)
        {
            var field = kind == StudentImageKinds.Photo ? StudentFields.Photo
                : kind == StudentImageKinds.Signature ? StudentFields.Signature
                : throw new InvalidOperationException("Unknown image.");
            var (link, s, editable) = await LoadAsync(user);
            if (!editable.Contains(field))
                throw new InvalidOperationException($"Your {Labels[field].ToLowerInvariant()} can't be changed through this link.");

            var data = image ?? "";
            var comma = data.IndexOf(',');
            if (comma >= 0) data = data[(comma + 1)..];
            byte[] bytes;
            try { bytes = Convert.FromBase64String(data); }
            catch (FormatException) { throw new InvalidOperationException("The picture could not be read. Try another one."); }
            if (bytes.Length == 0) throw new InvalidOperationException("Choose a picture first.");
            if (bytes.Length > MaxImageBytes) throw new InvalidOperationException("The picture must be less than 2 MB.");
            if (!Report.CollegeBranding.IsDecodableImage(bytes))
                throw new InvalidOperationException("That file isn't a picture we can use. Use a JPG or PNG.");

            var saved = await _storage.SaveAsync(bytes, FileStorage.StudentsFolder, $"{Guid.NewGuid()}_{(field == StudentFields.Photo ? "photo" : "sign")}.png");
            var old = field == StudentFields.Photo ? s.PhotoUrl : s.SignUrl;
            if (field == StudentFields.Photo) s.PhotoUrl = saved; else s.SignUrl = saved;
            var result = await FinishAsync(user, link, s, new List<(string, string?, string?)> { (field, old == null ? null : "previous", "new picture") });
            _storage.Delete(old);
            return result;
        }

        public async Task<(byte[] Bytes, string ContentType)?> ReadImageAsync(ClaimsPrincipal user, string kind)
        {
            var (_, s, _) = await LoadAsync(user);
            var path = kind == StudentImageKinds.Photo ? s.PhotoUrl : kind == StudentImageKinds.Signature ? s.SignUrl : null;
            var bytes = await _storage.ReadAllBytesAsync(path);
            if (bytes == null) return null;
            return (bytes, FileStorage.ImageContentType(path!) ?? "image/png");
        }

        /// <summary>Saves the changes, marks the link used, audits them and sends the notice; answers the fresh home.</summary>
        private async Task<StudentSaveResult> FinishAsync(ClaimsPrincipal user, TaskLink link, StudentMaster s, List<(string Field, string? Old, string? New)> changes)
        {
            var result = new StudentSaveResult { Changed = changes.Select(c => Labels[c.Field]).ToList() };
            if (changes.Count > 0)
            {
                var now = DateTime.UtcNow;
                link.UsedAt = now;
                var details = string.Join("; ", changes.Select(c => $"{Labels[c.Field]}: '{c.Old}' -> '{c.New}'"));
                _context.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    CollegeId = link.CollegeId,
                    Action = "UPDATE",
                    TableName = nameof(TaskLink),
                    RecordId = link.Id.ToString(),
                    PerformedBy = s.StdMstId,
                    UserType = StudentAccessAuth.RoleName,
                    Event = ChangedEvent,
                    Details = details.Length > 1000 ? details[..1000] : details,
                });
                await QueueNoticeAsync(link, s, result.Changed, now);
                await _context.SaveChangesAsync();
            }
            result.Home = await _signIn.HomeAsync(user);
            return result;
        }

        private async Task QueueNoticeAsync(TaskLink link, StudentMaster s, List<string> changed, DateTime now)
        {
            if (s.Email == null || link.CollegeId is not Guid collegeId) return;
            var since = now - NoticeGap;
            if (await _context.EmailQueue.AnyAsync(m => m.Kind == EmailKinds.Plain && m.RefId == link.Id && m.CreatedAt > since))
                return;
            var college = await StudentLinkEmails.CollegeNameAsync(_context, collegeId);
            var help = await _context.CollegeSettings.Where(x => x.CollegeId == collegeId).Select(x => x.ExamOfficeEmail).FirstOrDefaultAsync();
            var when = now.ToLocalTime().ToString("d MMM yyyy, h:mm tt", CultureInfo.InvariantCulture);
            var list = string.Join(", ", changed);
            var e = (Func<string, string>)WebUtility.HtmlEncode;
            var message = EmailLayout.Build(s.Email, $"{college}: your details were changed",
                $"<p>Dear {e(s.FirstName ?? "Student")},</p><p>Your student details at {e(college)} were changed on {e(when)}: <b>{e(list)}</b>.</p>"
                + "<p>If this was you, there is nothing to do. If it wasn't, contact your college office straight away.</p>",
                $"Dear {s.FirstName},\n\nYour student details at {college} were changed on {when}: {list}.\n\nIf this was you, there is nothing to do. If it wasn't, contact your college office straight away.",
                help);
            _outbox.Enqueue(collegeId, EmailKinds.Plain, link.Id, s.Email, EmailPriorities.Low, message.Subject, message.Html, message.Text);
        }
    }
}
