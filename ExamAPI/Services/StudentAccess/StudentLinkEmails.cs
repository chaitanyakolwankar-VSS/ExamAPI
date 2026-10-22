using System.Net;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.StudentAccess
{
    /// <summary>The emails of student access: the task link and the 6-digit code.</summary>
    public static class StudentLinkEmails
    {
        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

        public static async Task<string> CollegeNameAsync(ApplicationDbContext context, Guid? collegeId, CancellationToken ct = default) =>
            await context.Colleges.IgnoreQueryFilters().Where(c => c.CollegeId == collegeId).Select(c => c.Name).FirstOrDefaultAsync(ct)
                ?? "Your college";

        /// <summary>
        /// The link in the email. The token goes after "#": a browser never sends that part to the server, so it stays out
        /// of web server logs and Referer headers. Links sent earlier as /s/{token} still open.
        /// </summary>
        public static string LinkUrl(string baseUrl, string token) => $"{baseUrl.TrimEnd('/')}/s#{token}";

        /// <summary>"what the link is for", per task.</summary>
        private static string Purpose(TaskLink link, IReadOnlyCollection<string> editable, string? examName) => link.TaskTypeCode switch
        {
            TaskTypes.StudentMaster when editable.Count == 0 => "check your student details",
            TaskTypes.StudentMaster when editable.All(f => f is StudentFields.Photo or StudentFields.Signature) => "update your photo and signature",
            TaskTypes.StudentMaster => "check and update your student details",
            TaskTypes.HallTicket => string.IsNullOrWhiteSpace(examName) ? "download your hall ticket" : $"download your hall ticket for {examName}",
            _ => (link.TaskType?.Name ?? "complete a task").ToLowerInvariant(),
        };

        /// <summary>What the student does after opening the link, in order.</summary>
        private static List<string> Steps(TaskLink link, IReadOnlyCollection<string> editable)
        {
            var steps = new List<string> { "Open the link and confirm it's you with the one-time code sent to this email." };
            if (link.TaskTypeCode == TaskTypes.HallTicket)
            {
                steps.Add("If asked, add your photo or signature and save.");
                steps.Add("Download your hall ticket (PDF) and keep it. Print it for the exam.");
                return steps;
            }
            var pictures = editable.Count(f => f is StudentFields.Photo or StudentFields.Signature);
            var text = editable.Count - pictures;
            if (editable.Count == 0)
            {
                steps.Add("Check your details. If something is wrong, contact your college office.");
                return steps;
            }
            if (text > 0) steps.Add("Check your details and correct what's wrong.");
            if (pictures > 0) steps.Add(pictures == 2 ? "Tap your photo and signature on your card to add or change them." : $"Tap your {(editable.Contains(StudentFields.Photo) ? "photo" : "signature")} on your card to add or change it.");
            steps.Add("Tap Save. We email you what was saved.");
            return steps;
        }

        public static EmailMessage LinkMessage(string to, string college, StudentMaster student, TaskLink link, IReadOnlyCollection<string> editable, string url, string? examOfficeEmail, string? examName = null)
        {
            var purpose = Purpose(link, editable, examName);
            var hallTicket = link.TaskTypeCode == TaskTypes.HallTicket;
            var opens = hallTicket ? "your hall ticket" : "your details";
            var until = link.ExpiresAt.ToLocalTime().ToString("d MMM yyyy");
            var name = string.IsNullOrWhiteSpace(student.FirstName) ? "Student" : student.FirstName.Trim();
            var steps = Steps(link, editable);
            Func<string, string> e = s => WebUtility.HtmlEncode(s);
            var html =
                $"<p>Dear {e(name)},</p>"
                + $"<p>{e(college)} asks you to {e(purpose)}.</p>"
                + $"<p><a href=\"{e(url)}\" style=\"display:inline-block;background:#465fff;color:#ffffff;text-decoration:none;padding:10px 18px;border-radius:6px\">Open {(hallTicket ? "my hall ticket" : "my details")}</a></p>"
                + $"<p style=\"margin-bottom:4px\"><b>How to do it</b></p><ol style=\"margin-top:0;padding-left:20px\">{string.Concat(steps.Select(st => $"<li style=\"margin-bottom:4px\">{e(st)}</li>"))}</ol>"
                + $"<p>The link works until <b>{e(until)}</b>.</p>"
                + $"<p style=\"color:#6b7280\">Don't forward this email: the link opens {opens}.</p>";
            var text =
                $"Dear {name},\n\n{college} asks you to {purpose}.\n\nOpen this link:\n{url}\n\nHow to do it:\n"
                + string.Concat(steps.Select((st, i) => $"{i + 1}. {st}\n"))
                + $"\nThe link works until {until}.\n\n"
                + $"Don't forward this email: the link opens {opens}.";
            return EmailLayout.Build(to, $"{college}: {Capitalize(purpose)}", html, text, examOfficeEmail);
        }

        public static EmailMessage CodeMessage(string to, string college, string code, string? examOfficeEmail)
        {
            var minutes = (int)CodeLifetime.TotalMinutes;
            var html =
                $"<p>Your code is</p><p style=\"font-size:28px;letter-spacing:6px;font-weight:bold\">{code}</p>"
                + $"<p>It works for {minutes} minutes. If you didn't ask for it, ignore this email: nobody can open your details without it.</p>";
            var text = $"Your code is {code}\n\nIt works for {minutes} minutes. If you didn't ask for it, ignore this email: nobody can open your details without it.";
            return EmailLayout.Build(to, $"{code} is your {college} code", html, text, examOfficeEmail);
        }

        private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>Writes a queued task link email when it is sent: a new token is made then (an earlier one stops working).</summary>
    public sealed class TaskLinkEmailRenderer : IEmailBodyRenderer
    {
        private readonly ApplicationDbContext _context;
        private readonly IStudentAccessSecrets _secrets;

        public TaskLinkEmailRenderer(ApplicationDbContext context, IStudentAccessSecrets secrets)
        {
            _context = context;
            _secrets = secrets;
        }

        public string Kind => EmailKinds.TaskLink;

        public async Task<EmailMessage?> RenderAsync(EmailQueue item, string? examOfficeEmail, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var link = await _context.TaskLinks.IgnoreQueryFilters()
                .Include(l => l.Student).Include(l => l.Batch).Include(l => l.TaskType)
                .FirstOrDefaultAsync(l => l.Id == item.RefId, ct);
            // Stopped, closed, or nowhere to send it: not wanted any more.
            if (link?.Student?.Email == null || !StudentAccessAuth.IsOpen(link, now) || string.IsNullOrWhiteSpace(link.Batch?.LinkBaseUrl))
                return null;

            var token = _secrets.NewToken();
            link.TokenHash = _secrets.Hash(token);
            link.SentAt = now;
            await _context.SaveChangesAsync(ct);

            var college = await StudentLinkEmails.CollegeNameAsync(_context, link.CollegeId, ct);
            var examName = link.TaskTypeCode == TaskTypes.HallTicket && link.RefId != null
                ? await _context.Exams.IgnoreQueryFilters().Where(x => x.ExamId == link.RefId).Select(x => x.Name).FirstOrDefaultAsync(ct)
                : null;
            // The student's email now, not when it was queued: staff may have corrected it since.
            return StudentLinkEmails.LinkMessage(link.Student.Email, college, link.Student, link, StudentFields.Parse(link.Batch.EditableFields),
                StudentLinkEmails.LinkUrl(link.Batch.LinkBaseUrl, token), examOfficeEmail, examName);
        }
    }

    /// <summary>Writes a code email that could not go out at once: a fresh code is made then.</summary>
    public sealed class OtpEmailRenderer : IEmailBodyRenderer
    {
        /// <summary>A code asked for longer ago than this is not sent late; the student asks again.</summary>
        public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(20);

        private readonly ApplicationDbContext _context;
        private readonly IStudentAccessSecrets _secrets;

        public OtpEmailRenderer(ApplicationDbContext context, IStudentAccessSecrets secrets)
        {
            _context = context;
            _secrets = secrets;
        }

        public string Kind => EmailKinds.Otp;

        public async Task<EmailMessage?> RenderAsync(EmailQueue item, string? examOfficeEmail, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var challenge = await _context.OtpChallenges.IgnoreQueryFilters()
                .Include(o => o.TaskLink).ThenInclude(l => l!.Student)
                .FirstOrDefaultAsync(o => o.Id == item.RefId, ct);
            var link = challenge?.TaskLink;
            if (challenge == null || link?.Student?.Email == null || challenge.UsedAt != null
                || challenge.CreatedAt < now - GiveUpAfter || !StudentAccessAuth.IsOpen(link, now))
                return null;

            var code = _secrets.NewCode();
            challenge.CodeHash = _secrets.CodeHash(challenge.Id, code);
            challenge.ExpiresAt = now + StudentLinkEmails.CodeLifetime;
            challenge.AttemptCount = 0;
            await _context.SaveChangesAsync(ct);

            var college = await StudentLinkEmails.CollegeNameAsync(_context, link.CollegeId, ct);
            return StudentLinkEmails.CodeMessage(link.Student.Email, college, code, examOfficeEmail);
        }
    }
}
