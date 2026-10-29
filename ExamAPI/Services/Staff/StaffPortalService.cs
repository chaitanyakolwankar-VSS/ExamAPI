using System.Globalization;
using System.Security.Claims;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.StudentAccess;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Staff
{
    /// <summary>The person's details as the "Check your details" form shows and saves them.</summary>
    public class StaffDetailsView
    {
        public string Title { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string? MiddleName { get; set; }
        public string LastName { get; set; } = "";
        public Guid? DepartmentId { get; set; }
        public Guid? DesignationId { get; set; }
        public bool IsExternal { get; set; }
        public string? ExternalCollege { get; set; }
        public string? ExternalCity { get; set; }
    }

    public class StaffOptionItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class StaffHomeOptions
    {
        public List<string> Titles { get; set; } = new();
        public List<StaffOptionItem> Departments { get; set; } = new();
        public List<StaffOptionItem> Designations { get; set; } = new();
    }

    public class StaffHomeLetter
    {
        public Guid LetterId { get; set; }
        public string LetterNo { get; set; } = "";
        /// <summary>yyyy-MM-dd.</summary>
        public string LetterDate { get; set; } = "";
        /// <summary>"Supervision", "Examiner" or "Moderator".</summary>
        public string Kind { get; set; } = "";
        public string Title { get; set; } = "";
        public string ExamTitle { get; set; } = "";
        /// <summary>The letter belongs to the exam or duty chart this link was sent for.</summary>
        public bool Current { get; set; }
    }

    /// <summary>What the staff site shows after sign-in: who, which exam or duty chart (ExamTitle), the details to check and all the person's letters.</summary>
    public class StaffHome
    {
        public string FirstName { get; set; } = "";
        public string MaskedEmail { get; set; } = "";
        public string ExamTitle { get; set; } = "";
        public string CollegeName { get; set; } = "";
        public bool NeedsDetails { get; set; }
        public StaffDetailsView Details { get; set; } = new();
        public StaffHomeOptions Options { get; set; } = new();
        public List<StaffHomeLetter> Letters { get; set; } = new();
    }

    public interface IStaffPortalService
    {
        Task<StaffHome> HomeAsync(ClaimsPrincipal staff);
        Task<StaffHome> SaveDetailsAsync(ClaimsPrincipal staff, StaffDetailsView details);
        /// <summary>A live letter of the signed-in person as PDF. Throws KeyNotFoundException for anyone else's or a cancelled letter.</summary>
        Task<(byte[] Bytes, string FileName)> LetterPdfAsync(ClaimsPrincipal staff, Guid letterId);
    }

    /// <summary>
    /// The staff site after sign-in. Nothing here takes a staff id or an exam id from the browser: the session names
    /// the link, and the link names the person and the exam. Reads go through the tenant filter (the session's claims).
    /// </summary>
    public sealed class StaffPortalService : IStaffPortalService
    {
        private readonly ApplicationDbContext _context;
        private readonly IStaffLetterPdfService _pdf;

        public StaffPortalService(ApplicationDbContext context, IStaffLetterPdfService pdf)
        {
            _context = context;
            _pdf = pdf;
        }

        private async Task<StaffLink> LinkAsync(ClaimsPrincipal staff)
        {
            var linkId = StaffAccessAuth.Read(staff, StaffAccessAuth.LinkClaim) ?? throw new UnauthorizedAccessException();
            var link = await _context.StaffLinks.Include(l => l.Staff).FirstOrDefaultAsync(l => l.Id == linkId) ?? throw new UnauthorizedAccessException();
            if (!StaffAccessAuth.IsOpen(link, DateTime.UtcNow) || !StaffAccessAuth.StaffOk(link.Staff))
                throw new UnauthorizedAccessException();
            return link;
        }

        public async Task<StaffHome> HomeAsync(ClaimsPrincipal staff) => await BuildHomeAsync(await LinkAsync(staff));

        private async Task<StaffHome> BuildHomeAsync(StaffLink link)
        {
            var person = link.Staff!;
            var lines = await StaffLetterLine.LoadAsync(_context, _context.StaffLetters.Where(l => l.StaffId == person.StaffId), ignoreFilters: false);
            var examIds = lines.Select(l => l.Letter.ExamId).Append(link.ExamId).Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
            var exams = await _context.Exams.Where(e => examIds.Contains(e.ExamId)).Select(e => new { e.ExamId, e.Name, e.ExamType, e.CreatedAt }).ToListAsync();
            var chartIds = lines.Select(l => l.Letter.DutyChartId).Append(link.DutyChartId).Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
            var charts = await _context.DutyCharts.Where(c => chartIds.Contains(c.Id)).Select(c => new { c.Id, c.Name, c.CreatedAt }).ToListAsync();

            // A letter belongs to an exam (examiner, moderator) or a duty chart (supervision): newest first.
            var created = exams.ToDictionary(e => e.ExamId, e => e.CreatedAt);
            foreach (var chart in charts) created[chart.Id] = chart.CreatedAt;
            Guid? GroupOf(StaffLetterLine l) => l.Letter.DutyChartId ?? l.Letter.ExamId;

            string examTitle;
            var linkExam = exams.FirstOrDefault(e => e.ExamId == link.ExamId);
            var linkChart = charts.FirstOrDefault(c => c.Id == link.DutyChartId);
            if (linkChart != null) examTitle = linkChart.Name;
            else if (linkExam != null) examTitle = string.Join(' ', new[] { linkExam.Name, linkExam.ExamType }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
            else examTitle = lines.FirstOrDefault(l => GroupOf(l) == (link.DutyChartId ?? link.ExamId))?.Letter.ExamTitle ?? "your exam";

            int Rank(StaffLetterLine l) => l.Kind == "Supervision" ? 0 : l.Kind == "Examiner" ? 1 : 2;
            var ordered = lines
                .OrderByDescending(l => GroupOf(l) is Guid g ? created.GetValueOrDefault(g) : default).ThenBy(GroupOf)
                .ThenBy(Rank).ThenBy(l => l.Letter.SerialNo);

            var options = await _context.StaffOptions.OrderBy(o => o.Name).ToListAsync();
            List<StaffOptionItem> Items(string kind) => options.Where(o => o.Kind == kind)
                .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).Select(o => new StaffOptionItem { Id = o.Id, Name = o.Name }).ToList();

            return new StaffHome
            {
                FirstName = person.FirstName,
                MaskedEmail = StudentSignInService.MaskEmail(person.Email),
                ExamTitle = examTitle,
                CollegeName = await StudentLinkEmails.CollegeNameAsync(_context, link.CollegeId),
                NeedsDetails = link.DetailsConfirmedAt == null,
                Details = new StaffDetailsView
                {
                    Title = person.Title,
                    FirstName = person.FirstName,
                    MiddleName = person.MiddleName,
                    LastName = person.LastName,
                    DepartmentId = person.DepartmentId,
                    DesignationId = person.DesignationId,
                    IsExternal = person.IsExternal,
                    ExternalCollege = person.ExternalCollege,
                    ExternalCity = person.ExternalCity,
                },
                Options = new StaffHomeOptions
                {
                    Titles = StaffTitles.All.ToList(),
                    Departments = Items(StaffOptionKinds.Department),
                    Designations = Items(StaffOptionKinds.Designation),
                },
                Letters = ordered.Select(l => new StaffHomeLetter
                {
                    LetterId = l.Letter.Id,
                    LetterNo = l.Letter.LetterNo,
                    LetterDate = l.Letter.LetterDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Kind = l.Kind,
                    Title = l.Title,
                    ExamTitle = l.Letter.ExamTitle,
                    Current = GroupOf(l) == (link.DutyChartId ?? link.ExamId),
                }).ToList(),
            };
        }

        private static string Tidy(string? value) => string.Join(' ', (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        private static string? OrNull(string value) => value == "" ? null : value;

        /// <summary>
        /// Saves the person's own details to Staff Master (employee ID, email and whether they are external never change
        /// here; an external person may change their college and city). The name on their live letters follows. Checked like
        /// the Staff Master form.
        /// </summary>
        public async Task<StaffHome> SaveDetailsAsync(ClaimsPrincipal session, StaffDetailsView details)
        {
            var link = await LinkAsync(session);
            var person = link.Staff!;
            var titleTyped = Tidy(details.Title);
            var title = StaffTitles.Canonical(titleTyped);
            var first = Tidy(details.FirstName);
            var middle = OrNull(Tidy(details.MiddleName));
            var last = Tidy(details.LastName);
            var college = person.IsExternal ? OrNull(Tidy(details.ExternalCollege)) : null;
            var city = person.IsExternal ? OrNull(Tidy(details.ExternalCity)) : null;

            var errors = new List<string>();
            if (titleTyped == "") errors.Add("Choose your title.");
            else if (title == null) errors.Add($"Choose your title from the list ({string.Join(", ", StaffTitles.All)}).");
            if (first == "") errors.Add("Enter your first name.");
            if (first.Length > 50 || (middle?.Length ?? 0) > 50 || last.Length > 50) errors.Add("A name is longer than 50 characters.");
            if (person.IsExternal && college == null) errors.Add("Enter your college.");
            if ((college?.Length ?? 0) > 200 || (city?.Length ?? 0) > 100) errors.Add("The college or city is too long.");
            var options = await _context.StaffOptions.ToListAsync();
            if (details.DepartmentId is Guid dep && !options.Any(o => o.Id == dep && o.Kind == StaffOptionKinds.Department))
                errors.Add("Choose your department from the list.");
            if (details.DesignationId is Guid des && !options.Any(o => o.Id == des && o.Kind == StaffOptionKinds.Designation))
                errors.Add("Choose your designation from the list.");
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", errors));

            var changed = new List<string>();
            void Set<T>(string label, T current, T next, Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(current, next)) return;
                apply(next);
                changed.Add(label);
            }
            Set("title", person.Title, title!, v => person.Title = v);
            Set("first name", person.FirstName, first, v => person.FirstName = v);
            Set("middle name", person.MiddleName, middle, v => person.MiddleName = v);
            Set("last name", person.LastName, last, v => person.LastName = v);
            Set("department", person.DepartmentId, details.DepartmentId, v => person.DepartmentId = v);
            Set("designation", person.DesignationId, details.DesignationId, v => person.DesignationId = v);
            Set("college", person.ExternalCollege, college, v => person.ExternalCollege = v);
            Set("city", person.ExternalCity, city, v => person.ExternalCity = v);

            // The letters print the name as it was when they were written; now they follow the person's own correction.
            if (changed.Any(c => c.EndsWith("name") || c == "title"))
            {
                var name = StaffService.Letter(person.Title, person.FirstName, person.MiddleName, person.LastName);
                foreach (var letter in await _context.StaffLetters.Where(l => l.StaffId == person.StaffId).ToListAsync())
                    letter.StaffName = name;
            }

            link.DetailsConfirmedAt = DateTime.UtcNow;
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CollegeId = link.CollegeId,
                Action = "UPDATE",
                TableName = nameof(StaffMaster),
                RecordId = person.StaffId.ToString(),
                PerformedBy = person.StaffId,
                UserType = StaffAccessAuth.RoleName,
                Event = "StaffDetailsConfirmed",
                Details = changed.Count == 0 ? "Confirmed their details; nothing changed." : $"Confirmed their details; changed: {string.Join(", ", changed)}.",
            });
            await _context.SaveChangesAsync();
            return await BuildHomeAsync(link);
        }

        public async Task<(byte[] Bytes, string FileName)> LetterPdfAsync(ClaimsPrincipal session, Guid letterId)
        {
            var link = await LinkAsync(session);
            // Only a live letter of this person (the filter hides cancelled letters and other colleges').
            if (!await _context.StaffLetters.AnyAsync(l => l.Id == letterId && l.StaffId == link.StaffId))
                throw new KeyNotFoundException("Letter not found.");
            var pdf = await _pdf.OneAsync(letterId);

            if (link.DownloadedAt == null)
            {
                link.DownloadedAt = DateTime.UtcNow;
                _context.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    CollegeId = link.CollegeId,
                    Action = "UPDATE",
                    TableName = nameof(StaffLetter),
                    RecordId = letterId.ToString(),
                    PerformedBy = link.StaffId,
                    UserType = StaffAccessAuth.RoleName,
                    Event = "StaffLetterDownloaded",
                    Details = $"{link.Staff!.EmployeeCode ?? "A staff member"} downloaded a letter (the first through this link).",
                });
                await _context.SaveChangesAsync();
            }
            return pdf;
        }
    }
}
