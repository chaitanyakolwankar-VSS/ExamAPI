using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.StudentAccess
{
    public class SendLinksRequest
    {
        public Guid AYID { get; set; }
        public Guid? CourseId { get; set; }
        public string? SemesterId { get; set; }
        public string TaskType { get; set; } = TaskTypes.StudentMaster;
        /// <summary>The links work until the end of this many days from today.</summary>
        public int Days { get; set; } = TaskLinkService.DefaultDays;
        /// <summary><see cref="StudentFields"/> the students may change; empty = view only.</summary>
        public List<string>? EditableFields { get; set; }
        /// <summary>One student only, by Student ID or email (the branch and semester are then ignored); empty = the whole class.</summary>
        public string? Student { get; set; }
        /// <summary>Where the student site is, as the staff browser sees it; checked by <see cref="StudentSiteUrl"/>.</summary>
        public string? AppUrl { get; set; }
    }

    /// <summary>Hall ticket links for one released exam of a class (the Release Hall Ticket selection).</summary>
    public class HallTicketLinksRequest
    {
        public Guid Ayid { get; set; }
        public Guid CourseId { get; set; }
        public string Semester { get; set; } = "";
        public string Pattern { get; set; } = "";
        public Guid ExamId { get; set; }
        /// <summary>The links work until the end of this many days from today.</summary>
        public int Days { get; set; } = 30;
        /// <summary>Where the student site is, as the staff browser sees it; checked by <see cref="StudentSiteUrl"/>.</summary>
        public string? AppUrl { get; set; }
    }

    public class SendLinksResult
    {
        public Guid BatchId { get; set; }
        public int Queued { get; set; }
        public int NoEmail { get; set; }
        public int Flagged { get; set; }
        public int Inactive { get; set; }
        /// <summary>Students whose earlier link for the same task was stopped by this one.</summary>
        public int Replaced { get; set; }
        /// <summary>Over today's email limit: they go out tomorrow.</summary>
        public int GoTomorrow { get; set; }
        public string Message { get; set; } = "";
    }

    public class LinkBatchFilter
    {
        public Guid AYID { get; set; }
        public Guid? CourseId { get; set; }
        public string? SemesterId { get; set; }
    }

    /// <summary>What staff see for a link (<see cref="LinkStates"/>).</summary>
    public static class LinkStates
    {
        public const string Queued = "Queued";
        public const string Sent = "Sent";
        public const string EmailFailed = "EmailFailed";
        public const string Opened = "Opened";
        public const string Updated = "Updated";
        public const string Closed = "Closed";
        public const string Stopped = "Stopped";
    }

    public class LinkRow
    {
        public Guid Id { get; set; }
        public string StudentId { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string State { get; set; } = "";
        /// <summary>Why an email has not gone (failed, waiting for tomorrow, no account).</summary>
        public string? Note { get; set; }
        /// <summary>What the student changed through this link, newest first, e.g. "Phone: '' -> '9876543210'" (from the audit log).</summary>
        public string? Changes { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime? OpenedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class LinkBatchRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string TaskType { get; set; } = "";
        public DateTime? Deadline { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsOpen { get; set; }
        public List<string> EditableFields { get; set; } = new();
        public int Total { get; set; }
        public int Updated { get; set; }
        public int Opened { get; set; }
        public int NotOpened { get; set; }
        public int Failed { get; set; }
        public int Stopped { get; set; }
    }

    public class LinkBatchDetail : LinkBatchRow
    {
        public List<LinkRow> Links { get; set; } = new();
    }

    public interface ITaskLinkService
    {
        Task<SendLinksResult> SendAsync(SendLinksRequest request, string linkBaseUrl);
        /// <summary>Emails each student of a released exam the link to their hall ticket.</summary>
        Task<SendLinksResult> SendHallTicketsAsync(HallTicketLinksRequest request, string linkBaseUrl);
        Task<List<LinkBatchRow>> ListBatchesAsync(LinkBatchFilter filter);
        Task<LinkBatchDetail> GetBatchAsync(Guid batchId);
        Task<LinkBatchDetail> SetDeadlineAsync(Guid batchId, DateTime date);
        Task<int> StopBatchAsync(Guid batchId);
        /// <summary>
        /// Emails the link again (new token) to students who have not opened theirs, or with <paramref name="includeOpened"/>
        /// to everyone who has not saved anything yet; answers how many.
        /// </summary>
        Task<int> RemindAsync(Guid batchId, bool includeOpened = false);
        Task<LinkRow> StopLinkAsync(Guid linkId);
        /// <summary>An open link: emailed again (with a new token). A stopped or closed one: a new link in the same batch.</summary>
        Task<LinkRow> ResendAsync(Guid linkId);
    }

    /// <summary>
    /// Staff side of task links: send a class its links (one batch), follow who opened and completed them, stop one or
    /// all, move the deadline, remind, resend. A student has at most one open link per task: a new one stops the old.
    /// Emails are queued (<see cref="IEmailOutbox"/>); the token is made when each email is written. Audited.
    /// </summary>
    public sealed class TaskLinkService : ITaskLinkService
    {
        public const int DefaultDays = 14;
        public const int MaxDays = 60;
        public const int MaxDeadlineDays = 90;

        private readonly ApplicationDbContext _context;
        private readonly IEmailOutbox _outbox;
        private readonly IEmailAccountResolver _accounts;

        public TaskLinkService(ApplicationDbContext context, IEmailOutbox outbox, IEmailAccountResolver accounts)
        {
            _context = context;
            _outbox = outbox;
            _accounts = accounts;
        }

        /// <summary>The last second of a local calendar day, in UTC (links "work until 2 Nov").</summary>
        public static DateTime EndOfLocalDay(DateTime localDate) =>
            DateTime.SpecifyKind(localDate.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Local).ToUniversalTime();

        private Guid CollegeId => _context.CurrentCollegeId ?? throw new InvalidOperationException("Sign in as college staff to send links.");

        public async Task<SendLinksResult> SendAsync(SendLinksRequest request, string linkBaseUrl)
        {
            var collegeId = CollegeId;
            if (request.TaskType != TaskTypes.StudentMaster)
                throw new InvalidOperationException("Only Student Master links can be sent for now.");
            if (request.Days is < 1 or > MaxDays)
                throw new InvalidOperationException($"Choose between 1 and {MaxDays} days.");
            if (request.AYID == Guid.Empty)
                throw new InvalidOperationException("Choose the academic year.");
            await EnsureSenderAsync(collegeId);

            var taskType = await _context.TaskTypes.FirstAsync(t => t.Code == request.TaskType);
            var editable = StudentFields.Clean(request.EditableFields);
            var deadline = EndOfLocalDay(DateTime.Today.AddDays(request.Days));

            var query = from se in _context.StudentEligibilities
                        join sm in _context.StudentMasters on se.StudentId equals sm.StudentId
                        where se.AYID == request.AYID
                        select new { sm, se.CourseId, se.SemesterId };
            var single = string.IsNullOrWhiteSpace(request.Student) ? null : request.Student.Trim();
            if (single != null)
            {
                var email = single.ToLower();
                query = query.Where(x => x.sm.StudentId == single || (x.sm.Email != null && x.sm.Email.ToLower() == email));
            }
            else
            {
                if (request.CourseId is Guid course && course != Guid.Empty)
                    query = query.Where(x => x.CourseId == course);
                if (!string.IsNullOrWhiteSpace(request.SemesterId))
                    query = query.Where(x => x.SemesterId == request.SemesterId);
            }
            var found = await query.ToListAsync();
            var students = found.GroupBy(x => x.sm.StdMstId).Select(g => g.First().sm).ToList();
            if (students.Count == 0)
                throw new InvalidOperationException(single != null
                    ? $"No student with the ID or email \"{single}\" is enrolled in this academic year."
                    : "No students found for this selection.");
            if (single != null && students.Count > 1)
                throw new InvalidOperationException("More than one student has this email. Use the Student ID instead.");

            // A one-student send is filed under that student's class, so it shows with the class's sends.
            var courseId = single != null ? found[0].CourseId : request.CourseId is Guid c && c != Guid.Empty ? c : null;
            var semesterId = single != null ? found[0].SemesterId : string.IsNullOrWhiteSpace(request.SemesterId) ? null : request.SemesterId;

            var title = single != null
                ? $"{taskType.Name} — {StudentNames.Display(students[0])} ({students[0].StudentId})"
                : $"{taskType.Name} — {await ClassNameAsync(courseId, semesterId)}";
            var batch = new TaskBatch
            {
                Id = Guid.NewGuid(),
                CollegeId = collegeId,
                TaskTypeCode = taskType.Code,
                Title = title.Length > 200 ? title[..200] : title,
                Deadline = deadline,
                CourseId = courseId,
                SemesterId = semesterId,
                AYID = request.AYID,
                EditableFields = string.Join(',', editable),
                LinkBaseUrl = linkBaseUrl,
            };
            return await QueueBatchAsync(batch, students, single != null);
        }

        public async Task<SendLinksResult> SendHallTicketsAsync(HallTicketLinksRequest request, string linkBaseUrl)
        {
            var collegeId = CollegeId;
            if (request.Days is < 1 or > MaxDays)
                throw new InvalidOperationException($"Choose between 1 and {MaxDays} days.");
            await EnsureSenderAsync(collegeId);

            var exam = await _context.Exams.FirstOrDefaultAsync(e => e.ExamId == request.ExamId)
                ?? throw new InvalidOperationException("Exam not found.");
            var release = (await ExamAPI.Services.DeclareResult.DResultService.RowsByExam(_context, request.CourseId, request.Ayid, request.Semester, request.Pattern, new[] { exam.ExamId }))
                .GetValueOrDefault(exam.ExamId);
            if (release is not { ReleaseHallTicket: true })
                throw new InvalidOperationException("Release the hall ticket for this exam first.");

            // The students who have this hall ticket: a marks row for the exam (as Generate Hall Ticket prints them).
            var students = await _context.MarksMasters
                .Where(m => m.ExamId == exam.ExamId && m.AcademicYearAYID == request.Ayid && m.SemesterId == request.Semester && m.Pattern == request.Pattern)
                .Join(_context.StudentMasters, m => m.StdMstId, s => s.StdMstId, (m, s) => s)
                .Distinct().ToListAsync();
            if (students.Count == 0)
                throw new InvalidOperationException("No students have a hall ticket for this exam yet.");

            var title = $"Hall ticket — {exam.Name} — {await ClassNameAsync(request.CourseId, request.Semester)}";
            var batch = new TaskBatch
            {
                Id = Guid.NewGuid(),
                CollegeId = collegeId,
                TaskTypeCode = TaskTypes.HallTicket,
                Title = title.Length > 200 ? title[..200] : title,
                Deadline = EndOfLocalDay(DateTime.Today.AddDays(request.Days)),
                CourseId = request.CourseId,
                SemesterId = request.Semester,
                AYID = request.Ayid,
                RefId = exam.ExamId,
                // Only used when the photo or signature is missing: the hall ticket needs both.
                EditableFields = string.Join(',', StudentFields.Photo, StudentFields.Signature),
                LinkBaseUrl = linkBaseUrl,
            };
            return await QueueBatchAsync(batch, students, single: false);
        }

        /// <summary>
        /// Adds the batch and a link per student with a usable email, stops each one's earlier open link for the same
        /// task (and the same exam), queues the emails and audits the send.
        /// </summary>
        private async Task<SendLinksResult> QueueBatchAsync(TaskBatch batch, List<StudentMaster> students, bool single)
        {
            var collegeId = batch.CollegeId!.Value;
            var deadline = batch.Deadline!.Value;
            var now = DateTime.UtcNow;
            var result = new SendLinksResult();
            var targets = new List<StudentMaster>();
            foreach (var s in students)
            {
                if (!s.IsActive) result.Inactive++;
                else if (s.Email == null) result.NoEmail++;
                else if (s.EmailStatus == EmailVerification.Flagged) result.Flagged++;
                else targets.Add(s);
            }
            if (targets.Count == 0)
                throw new InvalidOperationException(!single
                    ? "None of these students has a usable email. Fix them under Check emails first."
                    : result.Inactive > 0 ? "This student is marked inactive." : "This student has no usable email. Fix it under Check emails first.");

            _context.TaskBatches.Add(batch);
            var ids = targets.Select(s => s.StdMstId).ToList();
            var earlier = await _context.TaskLinks
                .Where(l => ids.Contains(l.StdMstId) && l.TaskTypeCode == batch.TaskTypeCode && l.RefId == batch.RefId && l.Status == TaskLinkStatuses.Active)
                .ToListAsync();
            result.Replaced = earlier.Count(l => l.ExpiresAt > now);
            await StopAsync(earlier, now);

            var today = await _outbox.DailyStatusAsync(collegeId);
            foreach (var s in targets)
            {
                var link = NewLink(batch, s.StdMstId, deadline);
                _outbox.Enqueue(collegeId, EmailKinds.TaskLink, link.Id, s.Email!, EmailPriorities.Low);
            }
            result.BatchId = batch.Id;
            result.Queued = targets.Count;
            result.GoTomorrow = Math.Max(0, targets.Count - today.Remaining);

            var editable = StudentFields.Parse(batch.EditableFields);
            Audit(collegeId, batch.Id, "TaskLinksSent",
                $"{batch.Title}: {targets.Count} links until {deadline.ToLocalTime():d MMM yyyy}; editable: {(editable.Count == 0 ? "none" : string.Join(", ", editable))}; "
                + $"skipped {result.NoEmail} without email, {result.Flagged} flagged, {result.Inactive} inactive; replaced {result.Replaced}.");
            await _context.SaveChangesAsync();

            result.Message = (single
                    ? $"The link is on its way to {StudentNames.Display(targets[0])}."
                    : $"Links are on their way to {targets.Count} student{(targets.Count == 1 ? "" : "s")}.")
                + (result.GoTomorrow > 0 ? $" Today's email limit is reached for {result.GoTomorrow}; they go out tomorrow." : "");
            return result;
        }

        public async Task<List<LinkBatchRow>> ListBatchesAsync(LinkBatchFilter filter)
        {
            var query = _context.TaskBatches.Where(b => b.AYID == filter.AYID);
            if (filter.CourseId is Guid course && course != Guid.Empty)
                query = query.Where(b => b.CourseId == course);
            if (!string.IsNullOrWhiteSpace(filter.SemesterId))
                query = query.Where(b => b.SemesterId == filter.SemesterId);
            var batches = await query.OrderByDescending(b => b.CreatedAt).Take(50).ToListAsync();
            var rows = await RowsAsync(batches.Select(b => b.Id).ToList());
            return batches.Select(b => Summarize(new LinkBatchRow(), b, rows.Where(r => r.BatchId == b.Id).Select(r => r.Row).ToList())).ToList();
        }

        public async Task<LinkBatchDetail> GetBatchAsync(Guid batchId)
        {
            var batch = await FindBatchAsync(batchId);
            var rows = (await RowsAsync(new List<Guid> { batch.Id })).Select(r => r.Row)
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.StudentId, StringComparer.Ordinal).ToList();
            var detail = Summarize(new LinkBatchDetail(), batch, rows);
            detail.Links = rows;
            return detail;
        }

        public async Task<LinkBatchDetail> SetDeadlineAsync(Guid batchId, DateTime date)
        {
            var batch = await FindBatchAsync(batchId);
            if (date.Date < DateTime.Today || date.Date > DateTime.Today.AddDays(MaxDeadlineDays))
                throw new InvalidOperationException($"Choose a date from today up to {MaxDeadlineDays} days ahead.");
            var deadline = EndOfLocalDay(date);
            batch.Deadline = deadline;
            var links = await _context.TaskLinks.Where(l => l.BatchId == batchId && l.Status == TaskLinkStatuses.Active).ToListAsync();
            foreach (var link in links) link.ExpiresAt = deadline;
            Audit(batch.CollegeId, batch.Id, "TaskDeadlineChanged", $"{batch.Title}: links now work until {date:d MMM yyyy} ({links.Count} links).");
            await _context.SaveChangesAsync();
            return await GetBatchAsync(batchId);
        }

        public async Task<int> StopBatchAsync(Guid batchId)
        {
            var batch = await FindBatchAsync(batchId);
            var links = await _context.TaskLinks.Where(l => l.BatchId == batchId && l.Status == TaskLinkStatuses.Active).ToListAsync();
            await StopAsync(links, DateTime.UtcNow);
            Audit(batch.CollegeId, batch.Id, "TaskLinksStopped", $"{batch.Title}: {links.Count} links stopped.");
            await _context.SaveChangesAsync();
            return links.Count;
        }

        public async Task<int> RemindAsync(Guid batchId, bool includeOpened = false)
        {
            var batch = await FindBatchAsync(batchId);
            var collegeId = CollegeId;
            await EnsureSenderAsync(collegeId);
            var now = DateTime.UtcNow;
            var links = await _context.TaskLinks.Include(l => l.Student)
                .Where(l => l.BatchId == batchId && l.Status == TaskLinkStatuses.Active && l.ExpiresAt > now
                    && (includeOpened ? l.UsedAt == null : l.OpenedAt == null))
                .ToListAsync();
            var ids = links.Select(l => l.Id).ToList();
            var waiting = await _context.EmailQueue
                .Where(m => m.Kind == EmailKinds.TaskLink && m.RefId != null && ids.Contains(m.RefId.Value)
                    && (m.Status == EmailStatuses.Pending || m.Status == EmailStatuses.Sending))
                .Select(m => m.RefId!.Value).ToListAsync();
            var count = 0;
            foreach (var link in links.Where(l => !waiting.Contains(l.Id) && Sendable(l.Student)))
            {
                _outbox.Enqueue(collegeId, EmailKinds.TaskLink, link.Id, link.Student!.Email!, EmailPriorities.Low);
                count++;
            }
            if (count > 0)
            {
                Audit(collegeId, batch.Id, "TaskLinksReminded", $"{batch.Title}: link emailed again to {count} students who had not {(includeOpened ? "updated" : "opened")} it.");
                await _context.SaveChangesAsync();
            }
            return count;
        }

        public async Task<LinkRow> StopLinkAsync(Guid linkId)
        {
            var link = await _context.TaskLinks.FirstOrDefaultAsync(l => l.Id == linkId) ?? throw new KeyNotFoundException("Link not found.");
            if (link.Status == TaskLinkStatuses.Active)
            {
                await StopAsync(new List<TaskLink> { link }, DateTime.UtcNow);
                Audit(link.CollegeId, link.Id, "TaskLinkStopped", $"Link of student {link.StdMstId} stopped.");
                await _context.SaveChangesAsync();
            }
            return await RowAsync(link.Id);
        }

        public async Task<LinkRow> ResendAsync(Guid linkId)
        {
            var collegeId = CollegeId;
            var link = await _context.TaskLinks.Include(l => l.Student).Include(l => l.Batch)
                .FirstOrDefaultAsync(l => l.Id == linkId) ?? throw new KeyNotFoundException("Link not found.");
            if (!Sendable(link.Student))
                throw new InvalidOperationException("This student has no usable email. Fix it under Check emails first.");
            await EnsureSenderAsync(collegeId);
            var now = DateTime.UtcNow;

            if (StudentAccessAuth.IsOpen(link, now))
            {
                var pending = await _context.EmailQueue
                    .Where(m => m.Kind == EmailKinds.TaskLink && m.RefId == link.Id && m.Status == EmailStatuses.Pending).ToListAsync();
                foreach (var m in pending) m.Status = EmailStatuses.Cancelled;
                _outbox.Enqueue(collegeId, EmailKinds.TaskLink, link.Id, link.Student!.Email!, EmailPriorities.Low);
                Audit(collegeId, link.Id, "TaskLinkResent", $"Link of {link.Student.StudentId} emailed again.");
                await _context.SaveChangesAsync();
                return await RowAsync(link.Id);
            }

            var batch = link.Batch ?? throw new InvalidOperationException("This link can't be sent again.");
            if (batch.Deadline is not DateTime deadline || deadline <= now)
                throw new InvalidOperationException("This batch has closed. Move its deadline first.");
            var others = await _context.TaskLinks
                .Where(l => l.StdMstId == link.StdMstId && l.TaskTypeCode == link.TaskTypeCode && l.RefId == link.RefId && l.Status == TaskLinkStatuses.Active)
                .ToListAsync();
            await StopAsync(others, now);
            var fresh = NewLink(batch, link.StdMstId, deadline);
            _outbox.Enqueue(collegeId, EmailKinds.TaskLink, fresh.Id, link.Student!.Email!, EmailPriorities.Low);
            Audit(collegeId, fresh.Id, "TaskLinkResent", $"New link for {link.Student.StudentId} (the earlier one was {(link.Status == TaskLinkStatuses.Revoked ? "stopped" : "closed")}).");
            await _context.SaveChangesAsync();
            return await RowAsync(fresh.Id);
        }

        // ------------------------------------------------------------------ helpers

        private static bool Sendable(StudentMaster? s) => s is { IsActive: true, Email: not null } && s.EmailStatus != EmailVerification.Flagged;

        private TaskLink NewLink(TaskBatch batch, Guid stdMstId, DateTime expiresAt)
        {
            var link = new TaskLink
            {
                Id = Guid.NewGuid(),
                CollegeId = batch.CollegeId,
                TaskTypeCode = batch.TaskTypeCode,
                BatchId = batch.Id,
                StdMstId = stdMstId,
                RefId = batch.RefId,
                Status = TaskLinkStatuses.Active,
                ExpiresAt = expiresAt,
            };
            _context.TaskLinks.Add(link);
            return link;
        }

        /// <summary>Stops links: their sessions end and their unsent emails are cancelled (saved by the caller).</summary>
        private async Task StopAsync(List<TaskLink> links, DateTime now)
        {
            if (links.Count == 0) return;
            var ids = links.Select(l => l.Id).ToList();
            foreach (var link in links)
            {
                link.Status = TaskLinkStatuses.Revoked;
                link.RevokedAt = now;
            }
            foreach (var s in await _context.StudentSessions.Where(s => ids.Contains(s.TaskLinkId) && s.EndedAt == null).ToListAsync())
                s.EndedAt = now;
            foreach (var m in await _context.EmailQueue
                         .Where(m => m.Kind == EmailKinds.TaskLink && m.RefId != null && ids.Contains(m.RefId.Value) && m.Status == EmailStatuses.Pending)
                         .ToListAsync())
                m.Status = EmailStatuses.Cancelled;
        }

        private async Task EnsureSenderAsync(Guid collegeId)
        {
            string source;
            try
            {
                (_, source) = await _accounts.ForCollegeAsync(collegeId);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                source = EmailAccountSources.None;
            }
            if (source == EmailAccountSources.None)
                throw new InvalidOperationException("Email is not set up yet, so links can't be sent. Ask the platform admin to add the email account.");
        }

        private async Task<string> ClassNameAsync(Guid? courseId, string? semesterId)
        {
            var branch = courseId is Guid c && c != Guid.Empty
                ? await _context.CourseMasters.Where(x => x.CourseId == c).Select(x => x.Name).FirstOrDefaultAsync() ?? "Branch"
                : "All branches";
            return string.IsNullOrWhiteSpace(semesterId) ? branch : $"{branch}, semester {semesterId}";
        }

        private async Task<TaskBatch> FindBatchAsync(Guid batchId) =>
            await _context.TaskBatches.FirstOrDefaultAsync(b => b.Id == batchId) ?? throw new KeyNotFoundException("Batch not found.");

        private async Task<LinkRow> RowAsync(Guid linkId) =>
            (await RowsAsync(new List<Guid>(), new List<Guid> { linkId })).Single().Row;

        /// <summary>Every link of the batches (or the given links) as staff see it.</summary>
        private async Task<List<(Guid? BatchId, LinkRow Row)>> RowsAsync(List<Guid> batchIds, List<Guid>? linkIds = null)
        {
            var now = DateTime.UtcNow;
            var links = await (linkIds != null
                    ? _context.TaskLinks.Where(l => linkIds.Contains(l.Id))
                    : _context.TaskLinks.Where(l => l.BatchId != null && batchIds.Contains(l.BatchId.Value)))
                .Join(_context.StudentMasters, l => l.StdMstId, s => s.StdMstId, (l, s) => new { l, s })
                .ToListAsync();
            var ids = links.Select(x => x.l.Id).ToList();
            var idTexts = ids.Select(i => i.ToString()).ToList();
            var changes = (await _context.AuditLogs
                    .Where(a => a.Event == StudentDetailsService.ChangedEvent && a.RecordId != null && idTexts.Contains(a.RecordId))
                    .Select(a => new { a.RecordId, a.Details, a.Timestamp })
                    .ToListAsync())
                .GroupBy(a => a.RecordId!).ToDictionary(g => g.Key, g => string.Join("; ", g.OrderByDescending(a => a.Timestamp).Select(a => a.Details)));
            var mails = (await _context.EmailQueue
                    .Where(m => m.Kind == EmailKinds.TaskLink && m.RefId != null && ids.Contains(m.RefId.Value))
                    .Select(m => new { RefId = m.RefId!.Value, m.Status, m.LastError, m.CreatedAt })
                    .ToListAsync())
                .GroupBy(m => m.RefId).ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.CreatedAt).First());

            return links.Select(x =>
            {
                mails.TryGetValue(x.l.Id, out var mail);
                var (state, note) = x.l.Status == TaskLinkStatuses.Revoked ? (LinkStates.Stopped, (string?)null)
                    : x.l.UsedAt != null ? (LinkStates.Updated, null)
                    : x.l.ExpiresAt <= now ? (LinkStates.Closed, null)
                    : x.l.OpenedAt != null ? (LinkStates.Opened, null)
                    : mail?.Status == EmailStatuses.Failed ? (LinkStates.EmailFailed, mail.LastError)
                    : mail?.Status == EmailStatuses.Sent ? (LinkStates.Sent, null)
                    : (LinkStates.Queued, mail?.LastError);
                return (x.l.BatchId, new LinkRow
                {
                    Id = x.l.Id,
                    StudentId = x.s.StudentId,
                    Name = StudentNames.Display(x.s),
                    Email = x.s.Email,
                    State = state,
                    Note = note,
                    SentAt = mail?.Status == EmailStatuses.Sent ? x.l.SentAt : null,
                    OpenedAt = x.l.OpenedAt,
                    UpdatedAt = x.l.UsedAt,
                    ExpiresAt = x.l.ExpiresAt,
                    Changes = changes.GetValueOrDefault(x.l.Id.ToString()),
                });
            }).ToList();
        }

        private static T Summarize<T>(T row, TaskBatch batch, List<LinkRow> links) where T : LinkBatchRow
        {
            row.Id = batch.Id;
            row.Title = batch.Title ?? batch.TaskTypeCode;
            row.TaskType = batch.TaskTypeCode;
            row.Deadline = batch.Deadline;
            row.CreatedAt = batch.CreatedAt;
            row.IsOpen = batch.Deadline > DateTime.UtcNow && links.Any(l => l.State is not (LinkStates.Stopped or LinkStates.Closed));
            row.EditableFields = StudentFields.Parse(batch.EditableFields);
            row.Total = links.Count;
            row.Updated = links.Count(l => l.State == LinkStates.Updated);
            row.Opened = links.Count(l => l.State == LinkStates.Opened);
            row.Failed = links.Count(l => l.State == LinkStates.EmailFailed);
            row.Stopped = links.Count(l => l.State == LinkStates.Stopped);
            row.NotOpened = links.Count(l => l.State is LinkStates.Queued or LinkStates.Sent);
            return row;
        }

        private void Audit(Guid? collegeId, Guid recordId, string evt, string details) =>
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CollegeId = collegeId,
                Action = "UPDATE",
                TableName = nameof(TaskBatch),
                RecordId = recordId.ToString(),
                PerformedBy = _context.CurrentUserId,
                UserType = "Staff",
                Event = evt,
                Details = details.Length > 1000 ? details[..1000] : details,
            });
    }
}
