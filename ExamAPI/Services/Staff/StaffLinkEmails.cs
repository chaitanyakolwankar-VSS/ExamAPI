using System.Net;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using ExamAPI.Services.StudentAccess;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Staff
{
    /// <summary>One letter as the staff site and the email describe it.</summary>
    public sealed record StaffLetterLine(StaffLetter Letter, string? SubjectCode, string? SubjectName)
    {
        /// <summary>"Supervision", "Examiner" or "Moderator".</summary>
        public string Kind => Letter.LetterType switch
        {
            StaffLetterTypes.Examiner => "Examiner",
            StaffLetterTypes.Moderator => "Moderator",
            _ => "Supervision"
        };

        private string Subject => string.Join(' ', new[] { SubjectCode, SubjectName }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

        private string Dates
        {
            get
            {
                var n = Letter.Duties.Where(d => d.Kind != DutyKinds.RetiredValuation).Select(d => d.Date).Distinct().Count();
                return $"{n} date{(n == 1 ? "" : "s")}";
            }
        }

        /// <summary>The staff site's title: "Supervision duty — Junior Supervisor, 5 dates" or "Examiner — CSC601 System Programming".</summary>
        public string Title => Letter.LetterType == StaffLetterTypes.Supervision
            ? $"Supervision duty — {(string.IsNullOrWhiteSpace(Letter.Role) ? "" : Letter.Role + ", ")}{Dates}"
            : Subject == "" ? Kind : $"{Kind} — {Subject}";

        /// <summary>The email's line: "Supervision duty (Junior Supervisor, 5 dates)" or "Examiner — CSC601 System Programming".</summary>
        public string EmailLine => Letter.LetterType == StaffLetterTypes.Supervision
            ? $"Supervision duty ({(string.IsNullOrWhiteSpace(Letter.Role) ? "" : Letter.Role + ", ")}{Dates})"
            : Title;

        /// <summary>A person's live letters (the query decides which and whether the tenant filter applies), with their subjects.</summary>
        public static async Task<List<StaffLetterLine>> LoadAsync(ApplicationDbContext context, IQueryable<StaffLetter> letters, bool ignoreFilters, CancellationToken ct = default)
        {
            var list = await letters.Include(l => l.Duties).ToListAsync(ct);
            var ids = list.Where(l => l.SubjectId != null).Select(l => l.SubjectId!.Value).Distinct().ToList();
            var subjects = await (ignoreFilters ? context.SubjectMasters.IgnoreQueryFilters() : context.SubjectMasters)
                .Where(s => ids.Contains(s.SubjectId)).Select(s => new { s.SubjectId, s.SubjectCode, s.Name }).ToListAsync(ct);
            return list.Select(l =>
            {
                var s = l.SubjectId == null ? null : subjects.FirstOrDefault(x => x.SubjectId == l.SubjectId);
                return new StaffLetterLine(l, s?.SubjectCode, s?.Name);
            }).ToList();
        }
    }

    /// <summary>The signature at the end of the letters-link email: the college's own text, or "Exam Office" and the college name.</summary>
    public static class StaffEmailSignature
    {
        public const int MaxLength = 1000;

        /// <summary>Line ends tidied (spaces at the end of a line dropped), empty lines at the start and end dropped; "" when nothing is left.</summary>
        public static string Clean(string? text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToList();
            while (lines.Count > 0 && lines[0].Trim() == "") lines.RemoveAt(0);
            while (lines.Count > 0 && lines[^1].Trim() == "") lines.RemoveAt(lines.Count - 1);
            return string.Join("\n", lines);
        }

        /// <summary>What the email ends with, as plain text with "\n" between lines: the saved signature, or "Exam Office" and the college name when there is none.</summary>
        public static string Effective(string? saved, string college)
        {
            var text = Clean(saved);
            return text != "" ? text : $"Exam Office\n{college}";
        }

        /// <summary>The college's saved signature (the tenant filter decides whose); null when none.</summary>
        public static async Task<string?> SavedAsync(ApplicationDbContext context, Guid? collegeId, bool ignoreFilters, CancellationToken ct = default) =>
            await (ignoreFilters ? context.CollegeSettings.IgnoreQueryFilters().Where(s => s.CollegeId == collegeId && !s.IsDeleted) : context.CollegeSettings)
                .AsNoTracking().Select(s => s.LetterEmailSignature).FirstOrDefaultAsync(ct);
    }

    /// <summary>The emails of staff links: the letters link and the 6-digit code.</summary>
    public static class StaffLinkEmails
    {
        /// <summary>The link in the email. The token goes after "#": a browser never sends that part to the server.</summary>
        public static string LinkUrl(string baseUrl, string token) => $"{baseUrl.TrimEnd('/')}/t#{token}";

        public static EmailMessage LinkMessage(string to, string college, string firstName, string examTitle, IReadOnlyList<string> letters,
            string url, DateTime expiresAt, string? examOfficeEmail, string? signature = null)
        {
            var signed = StaffEmailSignature.Effective(signature, college);
            var until = expiresAt.ToLocalTime().ToString("d MMM yyyy");
            var name = string.IsNullOrWhiteSpace(firstName) ? "Sir / Madam" : firstName.Trim();
            var exam = string.IsNullOrWhiteSpace(examTitle) ? "the examinations" : examTitle.Trim();
            var steps = new[]
            {
                "Open the link and confirm it's you with the one-time code sent to this email.",
                "Check your details.",
                "Download your letters (PDF).",
            };
            Func<string, string> e = s => WebUtility.HtmlEncode(s);
            var html =
                $"<p>Dear {e(name)},</p>"
                + $"<p>{e(college)} has issued you letters for {e(exam)}:</p>"
                + $"<ul style=\"margin-top:0;padding-left:20px\">{string.Concat(letters.Select(l => $"<li style=\"margin-bottom:4px\">{e(l)}</li>"))}</ul>"
                + $"<p><a href=\"{e(url)}\" style=\"display:inline-block;background:#465fff;color:#ffffff;text-decoration:none;padding:10px 18px;border-radius:6px\">Open my letters</a></p>"
                + $"<p style=\"margin-bottom:4px\"><b>How to get them</b></p><ol style=\"margin-top:0;padding-left:20px\">{string.Concat(steps.Select(st => $"<li style=\"margin-bottom:4px\">{e(st)}</li>"))}</ol>"
                + $"<p>The link works until <b>{e(until)}</b>.</p>"
                + "<p style=\"color:#6b7280\">Don't forward this email: the link opens your letters.</p>"
                + $"<p>{e(signed).Replace("\n", "<br>")}</p>";
            var text =
                $"Dear {name},\n\n{college} has issued you letters for {exam}:\n"
                + string.Concat(letters.Select(l => $"- {l}\n"))
                + $"\nOpen this link:\n{url}\n\nHow to get them:\n"
                + string.Concat(steps.Select((st, i) => $"{i + 1}. {st}\n"))
                + $"\nThe link works until {until}.\n\n"
                + "Don't forward this email: the link opens your letters.\n\n"
                + signed;
            return EmailLayout.Build(to, $"{college}: your letters for {exam}", html, text, examOfficeEmail);
        }

        public static EmailMessage CodeMessage(string to, string college, string code, string? examOfficeEmail)
        {
            var minutes = (int)StudentLinkEmails.CodeLifetime.TotalMinutes;
            var html =
                $"<p>Your code is</p><p style=\"font-size:28px;letter-spacing:6px;font-weight:bold\">{code}</p>"
                + $"<p>It works for {minutes} minutes. If you didn't ask for it, ignore this email: nobody can open your letters without it.</p>";
            var text = $"Your code is {code}\n\nIt works for {minutes} minutes. If you didn't ask for it, ignore this email: nobody can open your letters without it.";
            return EmailLayout.Build(to, $"{code} is your {college} code", html, text, examOfficeEmail);
        }
    }

    /// <summary>Writes a queued staff letters email when it is sent: a new token is made then (an earlier one stops working).</summary>
    public sealed class StaffLinkEmailRenderer : IEmailBodyRenderer
    {
        private readonly ApplicationDbContext _context;
        private readonly IStudentAccessSecrets _secrets;

        public StaffLinkEmailRenderer(ApplicationDbContext context, IStudentAccessSecrets secrets)
        {
            _context = context;
            _secrets = secrets;
        }

        public string Kind => StaffEmailKinds.Link;

        public async Task<EmailMessage?> RenderAsync(EmailQueue item, string? examOfficeEmail, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var link = await _context.StaffLinks.IgnoreQueryFilters().Include(l => l.Staff).FirstOrDefaultAsync(l => l.Id == item.RefId, ct);
            // Stopped, replaced, closed, or nowhere to send it: not wanted any more.
            if (link?.Staff == null || !StaffAccessAuth.IsOpen(link, now) || !StaffAccessAuth.StaffOk(link.Staff) || string.IsNullOrWhiteSpace(link.LinkBaseUrl))
                return null;

            var mine = _context.StaffLetters.IgnoreQueryFilters().Where(l => l.CollegeId == link.CollegeId && l.StaffId == link.StaffId && !l.IsDeleted);
            var lines = await StaffLetterLine.LoadAsync(_context,
                (link.DutyChartId is Guid chart ? mine.Where(l => l.DutyChartId == chart) : mine.Where(l => l.ExamId == link.ExamId))
                    .OrderBy(l => l.LetterType).ThenBy(l => l.SerialNo), ignoreFilters: true, ct);
            // Nothing left to send (the letters were cancelled since).
            if (lines.Count == 0) return null;

            var token = _secrets.NewToken();
            link.TokenHash = _secrets.Hash(token);
            link.SentAt = now;
            await _context.SaveChangesAsync(ct);

            var college = await StudentLinkEmails.CollegeNameAsync(_context, link.CollegeId, ct);
            var examTitle = lines[0].Letter.ExamTitle;
            var signature = await StaffEmailSignature.SavedAsync(_context, link.CollegeId, ignoreFilters: true, ct);
            // The person's email now, not when it was queued: it may have been corrected since.
            return StaffLinkEmails.LinkMessage(link.Staff.Email, college, link.Staff.FirstName, examTitle,
                lines.OrderBy(l => l.Kind == "Supervision" ? 0 : l.Kind == "Examiner" ? 1 : 2).ThenBy(l => l.Letter.SerialNo).Select(l => l.EmailLine).ToList(),
                StaffLinkEmails.LinkUrl(link.LinkBaseUrl, token), link.ExpiresAt, examOfficeEmail, signature);
        }
    }

    /// <summary>Writes a staff code email that could not go out at once: a fresh code is made then.</summary>
    public sealed class StaffOtpEmailRenderer : IEmailBodyRenderer
    {
        private readonly ApplicationDbContext _context;
        private readonly IStudentAccessSecrets _secrets;

        public StaffOtpEmailRenderer(ApplicationDbContext context, IStudentAccessSecrets secrets)
        {
            _context = context;
            _secrets = secrets;
        }

        public string Kind => StaffEmailKinds.Otp;

        public async Task<EmailMessage?> RenderAsync(EmailQueue item, string? examOfficeEmail, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var challenge = await _context.StaffOtps.IgnoreQueryFilters()
                .Include(o => o.Link).ThenInclude(l => l!.Staff)
                .FirstOrDefaultAsync(o => o.Id == item.RefId, ct);
            var link = challenge?.Link;
            if (challenge == null || link?.Staff == null || challenge.UsedAt != null
                || challenge.CreatedAt < now - OtpEmailRenderer.GiveUpAfter || !StaffAccessAuth.IsOpen(link, now) || !StaffAccessAuth.StaffOk(link.Staff))
                return null;

            var code = _secrets.NewCode();
            challenge.CodeHash = _secrets.CodeHash(challenge.Id, code);
            challenge.ExpiresAt = now + StudentLinkEmails.CodeLifetime;
            challenge.AttemptCount = 0;
            await _context.SaveChangesAsync(ct);

            var college = await StudentLinkEmails.CollegeNameAsync(_context, link.CollegeId, ct);
            return StaffLinkEmails.CodeMessage(link.Staff.Email, college, code, examOfficeEmail);
        }
    }
}
