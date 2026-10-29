using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Email;
using ExamAPI.Services.StudentAccess;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Staff
{
    /// <summary>
    /// Who to email: the letters of one duty chart (supervision) or of one exam (examiner and moderator), optionally only some
    /// people. Exactly one of <see cref="ExamId"/> and <see cref="DutyChartId"/>.
    /// </summary>
    public class StaffEmailRequest
    {
        /// <summary>Any branch's copy of the exam; for appointment letters.</summary>
        public Guid? ExamId { get; set; }
        /// <summary>The duty chart; for supervision letters.</summary>
        public Guid? DutyChartId { get; set; }
        /// <summary>"supervision" (needs a duty chart), "appointment" (examiner and moderator letters; needs an exam) or "all" (the letters of whichever was given).</summary>
        public string Kind { get; set; } = "all";
        /// <summary>Only these people. Absent: everyone with letters of that kind; an empty list: nobody.</summary>
        public List<Guid>? StaffIds { get; set; }
        /// <summary>Leave out people whose latest link was already emailed.</summary>
        public bool OnlyNotSent { get; set; }
        /// <summary>The address of the staff site as the browser knows it (checked by <see cref="StudentSiteUrl"/>); sending only.</summary>
        public string? AppUrl { get; set; }
    }

    public class StaffEmailPreview
    {
        /// <summary>People with live letters of that kind in the exam or chart (and in StaffIds, when given).</summary>
        public int People { get; set; }
        /// <summary>Of those, people who can be emailed (on the staff list, with a valid email).</summary>
        public int WithEmail { get; set; }
        /// <summary>Of those who can be emailed, people whose latest link was emailed (or is waiting to go) and still works.</summary>
        public int AlreadySent { get; set; }
        /// <summary>Their letters of that kind.</summary>
        public int Letters { get; set; }
        /// <summary>Names of the people who can't be emailed.</summary>
        public List<string> NoEmail { get; set; } = new();
        /// <summary>The college can send email (an account of its own or the platform default).</summary>
        public bool SenderReady { get; set; }
    }

    public class StaffEmailResult
    {
        public int Queued { get; set; }
        public int Skipped { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>The latest link of one person for an exam or a duty chart, as the Letters tab shows it.</summary>
    public class StaffLinkStatus
    {
        public Guid StaffId { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime? OpenedAt { get; set; }
        public DateTime? DownloadedAt { get; set; }
    }

    public interface IStaffLinkService
    {
        Task<StaffEmailPreview> PreviewAsync(StaffEmailRequest request);
        Task<StaffEmailResult> SendAsync(StaffEmailRequest request, string linkBaseUrl);
        Task<List<StaffLinkStatus>> StatusAsync(Guid? examId, Guid? dutyChartId);
    }

    /// <summary>
    /// The exam office emails staff their letters: one link per person for the exam or duty chart (a newer one stops the older), the
    /// email is written when it is sent (so it holds the token and the queue never does). Signed-in office staff only;
    /// every query goes through the tenant filter.
    /// </summary>
    public sealed class StaffLinkService : IStaffLinkService
    {
        public const int MinDays = 7;
        public const int AfterExamDays = 30;

        private readonly ApplicationDbContext _context;
        private readonly IEmailOutbox _outbox;
        private readonly IEmailAccountResolver _accounts;

        public StaffLinkService(ApplicationDbContext context, IEmailOutbox outbox, IEmailAccountResolver accounts)
        {
            _context = context;
            _outbox = outbox;
            _accounts = accounts;
        }

        private Guid CollegeId => _context.CurrentCollegeId ?? throw new InvalidOperationException("Sign in as college staff to send letters.");

        private sealed record Person(Guid StaffId, StaffMaster? Staff, string Name, int Letters)
        {
            public bool Usable => Staff != null && StaffAccessAuth.StaffOk(Staff) && StudentEmail.IsValid(StudentEmail.Normalize(Staff.Email));
        }

        /// <summary>What the links are for: an exam (its canonical copy and all copies) or a duty chart.</summary>
        private sealed record Scope(Guid? Exam, List<Guid> Copies, Guid? Chart, DateTime? ChartTo)
        {
            public Guid Id => (Exam ?? Chart)!.Value;
        }

        private sealed record Selection(Scope Scope, List<Person> People);

        private static IReadOnlyList<string> TypesOf(string? kind) => (kind ?? "").Trim().ToLowerInvariant() switch
        {
            "supervision" => new[] { StaffLetterTypes.Supervision },
            "appointment" => new[] { StaffLetterTypes.Examiner, StaffLetterTypes.Moderator },
            "all" => new[] { StaffLetterTypes.Supervision, StaffLetterTypes.Examiner, StaffLetterTypes.Moderator },
            _ => throw new InvalidOperationException("Choose supervision letters, examiner letters or all letters."),
        };

        /// <summary>Which exam or duty chart the request is for, and the letter types of its kind.</summary>
        private async Task<(Scope Scope, IReadOnlyList<string> Types)> ScopeAsync(Guid? examId, Guid? dutyChartId, string? kind)
        {
            var types = TypesOf(kind);
            if ((examId == null) == (dutyChartId == null))
                throw new InvalidOperationException("Choose a duty chart (for supervision letters) or an exam (for examiner letters).");

            if (dutyChartId is Guid chartId)
            {
                if (!types.Contains(StaffLetterTypes.Supervision))
                    throw new InvalidOperationException("Examiner and moderator letters belong to an exam, not a duty chart.");
                var chart = await _context.DutyCharts.AsNoTracking().Where(c => c.Id == chartId).Select(c => new { c.Id, c.ToDate }).FirstOrDefaultAsync()
                    ?? throw new KeyNotFoundException("Duty chart not found.");
                return (new Scope(null, new List<Guid>(), chart.Id, chart.ToDate), new[] { StaffLetterTypes.Supervision });
            }

            if (!types.Any(t => t != StaffLetterTypes.Supervision))
                throw new InvalidOperationException("Supervision letters belong to a duty chart, not an exam.");
            var copies = await ExamGroups.CopyIdsAsync(_context, examId!.Value);
            if (copies.Count == 0) throw new KeyNotFoundException("Exam not found.");
            var canonical = await ExamGroups.CanonicalIdAsync(_context, examId.Value) ?? examId.Value;
            return (new Scope(canonical, copies, null, null), types.Where(t => t != StaffLetterTypes.Supervision).ToList());
        }

        private static IQueryable<StaffLetter> LettersOf(IQueryable<StaffLetter> letters, Scope scope) =>
            scope.Chart is Guid chart
                ? letters.Where(l => l.DutyChartId == chart)
                : letters.Where(l => l.ExamId != null && scope.Copies.Contains(l.ExamId.Value));

        private static IQueryable<StaffLink> LinksOf(IQueryable<StaffLink> links, Scope scope) =>
            scope.Chart is Guid chart
                ? links.Where(l => l.DutyChartId == chart)
                : links.Where(l => l.ExamId != null && scope.Copies.Contains(l.ExamId.Value));

        /// <summary>The people with live letters of the kind in the exam (any branch's copy) or duty chart, filtered to the given people.</summary>
        private async Task<Selection> SelectAsync(StaffEmailRequest request)
        {
            var (scope, types) = await ScopeAsync(request.ExamId, request.DutyChartId, request.Kind);

            var letters = LettersOf(_context.StaffLetters.AsNoTracking(), scope).Where(l => types.Contains(l.LetterType));
            if (request.StaffIds != null)
            {
                var wanted = request.StaffIds;
                letters = letters.Where(l => wanted.Contains(l.StaffId));
            }
            var rows = await letters.Select(l => new { l.StaffId, l.StaffName }).ToListAsync();
            var ids = rows.Select(r => r.StaffId).Distinct().ToList();
            var staff = await _context.StaffMasters.AsNoTracking().Where(s => ids.Contains(s.StaffId)).ToDictionaryAsync(s => s.StaffId);

            var people = rows.GroupBy(r => r.StaffId)
                .Select(g =>
                {
                    staff.TryGetValue(g.Key, out var s);
                    return new Person(g.Key, s, s == null ? g.First().StaffName : StaffService.Letter(s.Title, s.FirstName, s.MiddleName, s.LastName), g.Count());
                })
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return new Selection(scope, people);
        }

        private async Task<bool> SenderReadyAsync(Guid collegeId)
        {
            try
            {
                var (_, source) = await _accounts.ForCollegeAsync(collegeId);
                return source != EmailAccountSources.None;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return false; // a password the server can no longer read counts as not set up
            }
        }

        /// <summary>Each person's latest link for the exam or duty chart, with its email's state.</summary>
        private async Task<Dictionary<Guid, (StaffLink Link, string? MailStatus, DateTime? MailSentAt)>> LatestAsync(Scope scope, List<Guid>? staffIds = null)
        {
            var links = await LinksOf(_context.StaffLinks.AsNoTracking(), scope)
                .Where(l => staffIds == null || staffIds.Contains(l.StaffId))
                .ToListAsync();
            var latest = links.GroupBy(l => l.StaffId).Select(g => g.OrderByDescending(l => l.CreatedAt).First()).ToList();
            var ids = latest.Select(l => l.Id).ToList();
            var mails = (await _context.EmailQueue.AsNoTracking()
                    .Where(m => m.Kind == StaffEmailKinds.Link && m.RefId != null && ids.Contains(m.RefId.Value))
                    .Select(m => new { RefId = m.RefId!.Value, m.Status, m.SentAt, m.CreatedAt })
                    .ToListAsync())
                .GroupBy(m => m.RefId).ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.CreatedAt).First());
            return latest.ToDictionary(l => l.StaffId, l =>
            {
                mails.TryGetValue(l.Id, out var mail);
                return (l, mail?.Status, mail?.SentAt);
            });
        }

        /// <summary>The latest link was emailed (or its email is waiting to go) and still lets the person in.</summary>
        private static bool Emailed((StaffLink Link, string? MailStatus, DateTime? MailSentAt) x, DateTime now) =>
            StaffAccessAuth.IsOpen(x.Link, now) && x.MailStatus is EmailStatuses.Sent or EmailStatuses.Pending or EmailStatuses.Sending;

        public async Task<StaffEmailPreview> PreviewAsync(StaffEmailRequest request)
        {
            var collegeId = CollegeId;
            var selection = await SelectAsync(request);
            var usable = selection.People.Where(p => p.Usable).ToList();
            var latest = await LatestAsync(selection.Scope, usable.Select(p => p.StaffId).ToList());
            var now = DateTime.UtcNow;
            return new StaffEmailPreview
            {
                People = selection.People.Count,
                WithEmail = usable.Count,
                AlreadySent = usable.Count(p => latest.TryGetValue(p.StaffId, out var x) && Emailed(x, now)),
                Letters = selection.People.Sum(p => p.Letters),
                NoEmail = selection.People.Where(p => !p.Usable).Select(p => p.Name).ToList(),
                SenderReady = await SenderReadyAsync(collegeId),
            };
        }

        public async Task<StaffEmailResult> SendAsync(StaffEmailRequest request, string linkBaseUrl)
        {
            var collegeId = CollegeId;
            var selection = await SelectAsync(request);
            if (selection.People.Count == 0)
                throw new InvalidOperationException("There are no letters to email for this selection.");
            if (!await SenderReadyAsync(collegeId))
                throw new InvalidOperationException("Email is not set up yet, so letters can't be sent. Ask the platform admin to add the email account.");

            var now = DateTime.UtcNow;
            var usable = selection.People.Where(p => p.Usable).ToList();
            var noEmail = selection.People.Count - usable.Count;
            var latest = await LatestAsync(selection.Scope, usable.Select(p => p.StaffId).ToList());
            var alreadySent = request.OnlyNotSent ? usable.Where(p => latest.TryGetValue(p.StaffId, out var x) && Emailed(x, now)).ToList() : new List<Person>();
            var targets = usable.Except(alreadySent).ToList();
            if (targets.Count == 0)
                throw new InvalidOperationException(usable.Count == 0
                    ? "None of these people has a usable email on the staff list."
                    : "Everyone has been emailed already.");

            // Their earlier open links stop (the sessions end, the emails not yet sent are cancelled): one link per person and exam or chart.
            var scope = selection.Scope;
            var targetIds = targets.Select(p => p.StaffId).ToList();
            var earlier = await LinksOf(_context.StaffLinks, scope)
                .Where(l => targetIds.Contains(l.StaffId) && l.Status == StaffLinkStatuses.Active)
                .ToListAsync();
            var replaced = earlier.Count(l => l.ExpiresAt > now);
            await StopAsync(earlier, now);

            var expires = await ExpiryAsync(scope, now);
            var today = await _outbox.DailyStatusAsync(collegeId);
            foreach (var person in targets)
            {
                var link = new StaffLink
                {
                    Id = Guid.NewGuid(),
                    CollegeId = collegeId,
                    StaffId = person.StaffId,
                    ExamId = scope.Exam,
                    DutyChartId = scope.Chart,
                    Status = StaffLinkStatuses.Active,
                    ExpiresAt = expires,
                    CreatedAt = now,
                    LinkBaseUrl = linkBaseUrl,
                };
                _context.StaffLinks.Add(link);
                _outbox.Enqueue(collegeId, StaffEmailKinds.Link, link.Id, person.Staff!.Email, EmailPriorities.Low);
            }

            var skipped = noEmail + alreadySent.Count;
            var goTomorrow = Math.Max(0, targets.Count - today.Remaining);
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CollegeId = collegeId,
                Action = "UPDATE",
                TableName = nameof(StaffLink),
                RecordId = scope.Id.ToString(),
                PerformedBy = _context.CurrentUserId,
                UserType = "Staff",
                Event = "StaffLettersEmailed",
                Details = $"{request.Kind.Trim().ToLowerInvariant()} letters: {targets.Count} links until {expires.ToLocalTime():d MMM yyyy}; skipped {noEmail} without a usable email, {alreadySent.Count} already emailed; replaced {replaced}.",
            });
            await _context.SaveChangesAsync();

            return new StaffEmailResult
            {
                Queued = targets.Count,
                Skipped = skipped,
                Message = $"Letters are on their way to {targets.Count} {(targets.Count == 1 ? "person" : "people")}."
                    + (skipped > 0 ? $" {skipped} left out." : "")
                    + (goTomorrow > 0 ? $" Today's email limit is reached for {goTomorrow}; they go out tomorrow." : ""),
            };
        }

        public async Task<List<StaffLinkStatus>> StatusAsync(Guid? examId, Guid? dutyChartId)
        {
            _ = CollegeId;
            var (scope, _) = await ScopeAsync(examId, dutyChartId, "all");
            var latest = await LatestAsync(scope);
            return latest.Select(kv => new StaffLinkStatus
            {
                StaffId = kv.Key,
                SentAt = kv.Value.MailStatus == EmailStatuses.Sent ? kv.Value.MailSentAt : null,
                OpenedAt = kv.Value.Link.OpenedAt,
                DownloadedAt = kv.Value.Link.DownloadedAt,
            }).ToList();
        }

        /// <summary>30 days after the chart's last day (or the exam's last timetable date), or 30 days from now when the exam has none; never less than a week from now.</summary>
        private async Task<DateTime> ExpiryAsync(Scope scope, DateTime now)
        {
            var copies = scope.Copies;
            var last = scope.ChartTo?.Date ?? DateTime.MaxValue;
            if (scope.Chart == null)
            {
                var dates = await _context.TimeTables.AsNoTracking()
                    .Where(t => t.ExamId != null && copies.Contains(t.ExamId.Value) && t.Date != null && t.Date != "")
                    .Select(t => t.Date!).ToListAsync();
                last = dates.Select(ExamSlotOrder.Date).Where(d => d != DateTime.MaxValue).DefaultIfEmpty(DateTime.MaxValue).Max();
            }
            var wanted = last == DateTime.MaxValue
                ? TaskLinkService.EndOfLocalDay(DateTime.Today.AddDays(AfterExamDays))
                : TaskLinkService.EndOfLocalDay(last.AddDays(AfterExamDays));
            var least = TaskLinkService.EndOfLocalDay(DateTime.Today.AddDays(MinDays));
            return wanted < least ? least : wanted;
        }

        /// <summary>Stops links: their sessions end and their unsent emails are cancelled (saved by the caller).</summary>
        private async Task StopAsync(List<StaffLink> links, DateTime now)
        {
            if (links.Count == 0) return;
            var ids = links.Select(l => l.Id).ToList();
            foreach (var link in links)
            {
                link.Status = StaffLinkStatuses.Revoked;
                link.RevokedAt = now;
            }
            foreach (var s in await _context.StaffSessions.Where(s => ids.Contains(s.LinkId) && s.EndedAt == null).ToListAsync())
                s.EndedAt = now;
            foreach (var m in await _context.EmailQueue
                         .Where(m => m.Kind == StaffEmailKinds.Link && m.RefId != null && ids.Contains(m.RefId.Value) && m.Status == EmailStatuses.Pending)
                         .ToListAsync())
                m.Status = EmailStatuses.Cancelled;
        }
    }
}
