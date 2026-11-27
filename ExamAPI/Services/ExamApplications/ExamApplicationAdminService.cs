using ClosedXML.Excel;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.ExamApplications
{
    public class ExamApplicationRow
    {
        public Guid StdMstId { get; set; }
        public string StudentId { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string? Category { get; set; }
        /// <summary>Not sent, Sent, Opened, Stopped (the latest link for this exam).</summary>
        public string LinkState { get; set; } = "Not sent";
        public DateTime? LinkSentAt { get; set; }
        public Guid? ApplicationId { get; set; }
        /// <summary><see cref="ExamApplicationStatuses"/>, or null when they have not applied.</summary>
        public string? Status { get; set; }
        public int Subjects { get; set; }
        public decimal? Amount { get; set; }
        public string? ReceiptNo { get; set; }
        public DateTime? PaidAt { get; set; }
        public string? PaidVia { get; set; }
        public string? PaymentReference { get; set; }
        public string? LastPaymentStatus { get; set; }
        public string? AssignError { get; set; }
        public bool PaidTwice { get; set; }
        public string? Note { get; set; }
    }

    public class ExamApplicationList
    {
        public string ExamName { get; set; } = "";
        public string Kind { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public bool FeesSet { get; set; }
        public DateTime? ClosesAt { get; set; }
        /// <summary>A gateway account is set up for this college (its own or the platform default).</summary>
        public bool OnlinePaymentReady { get; set; }
        public int LinksSent { get; set; }
        public int Applied { get; set; }
        public int Paid { get; set; }
        public int Assigned { get; set; }
        public int NeedAttention { get; set; }
        public decimal Collected { get; set; }
        public List<ExamApplicationRow> Rows { get; set; } = new();
    }

    public class FeeReportFilter
    {
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }
        public Guid? ExamId { get; set; }
        public string? Kind { get; set; }
    }

    public class FeeReportRow
    {
        public string ReceiptNo { get; set; } = "";
        public DateTime PaidAt { get; set; }
        public string StudentId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Exam { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public string? Semester { get; set; }
        public int Subjects { get; set; }
        public decimal Amount { get; set; }
        public string PaidVia { get; set; } = "";
        /// <summary>Order number and gateway transaction (online) or the office reference.</summary>
        public string? Reference { get; set; }
        public string? GatewayTxnId { get; set; }
        public string Status { get; set; } = "";
    }

    public class FeeReport
    {
        public List<FeeReportRow> Rows { get; set; } = new();
        public decimal Total { get; set; }
        public decimal Online { get; set; }
        public decimal Office { get; set; }
        public int Waived { get; set; }
    }

    public interface IExamApplicationAdminService
    {
        /// <summary>Every student with a link or an application for this exam, with where they are.</summary>
        Task<ExamApplicationList> ListAsync(Guid examId);
        Task<ExamApplicationRow> PayAtOfficeAsync(Guid applicationId, string reference);
        Task<ExamApplicationRow> RetryAssignAsync(Guid applicationId);
        Task<ExamApplicationRow> CancelAsync(Guid applicationId, string reason);
        /// <summary>A double payment was refunded at the gateway: clears the flag, keeps the note.</summary>
        Task<ExamApplicationRow> RefundDoneAsync(Guid applicationId, string note);
        Task<(byte[] Pdf, string FileName)> ReceiptAsync(Guid applicationId);
        Task<FeeReport> ReportAsync(FeeReportFilter filter);
        Task<(byte[] Content, string FileName)> ReportExcelAsync(FeeReportFilter filter);
    }

    /// <summary>Staff (college from the token): follow an exam's applications, take office payments, fix what needs a hand.</summary>
    public sealed class ExamApplicationAdminService : IExamApplicationAdminService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPaymentService _payments;
        private readonly IExamApplicationAssigner _assigner;
        private readonly IPaymentAccountResolver _accounts;

        public ExamApplicationAdminService(ApplicationDbContext context, IPaymentService payments, IExamApplicationAssigner assigner, IPaymentAccountResolver accounts)
        {
            _context = context;
            _payments = payments;
            _assigner = assigner;
            _accounts = accounts;
        }

        private Guid CollegeId => _context.CurrentCollegeId
            ?? throw new UnauthorizedAccessException("Sign in as staff of a college to see exam applications.");

        public async Task<ExamApplicationList> ListAsync(Guid examId)
        {
            var collegeId = CollegeId;
            var exam = await _context.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.ExamId == examId)
                ?? throw new KeyNotFoundException("Exam not found.");
            var kind = ExamFeeService.KindOf(exam) ?? throw new InvalidOperationException("Students can apply only for an A.T.K.T, Re-Exam or Revaluation exam.");
            var setup = await _context.ExamFeeSetups.AsNoTracking().FirstOrDefaultAsync(s => s.ExamId == examId);

            var apps = await _context.ExamApplications.AsNoTracking().Include(a => a.Subjects)
                .Where(a => a.TargetExamId == examId).ToListAsync();
            var appIds = apps.Select(a => a.Id).ToList();
            var lastOrders = (await _context.PaymentOrders.AsNoTracking()
                    .Where(o => appIds.Contains(o.ApplicationId))
                    .Select(o => new { o.ApplicationId, o.Status, o.CreatedAt }).ToListAsync())
                .GroupBy(o => o.ApplicationId).ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.CreatedAt).First().Status);
            var links = (await _context.TaskLinks.AsNoTracking()
                    .Where(l => l.TaskTypeCode == TaskTypes.AtktPayment && l.RefId == examId)
                    .Select(l => new { l.StdMstId, l.Status, l.SentAt, l.OpenedAt, l.CreatedAt }).ToListAsync())
                .GroupBy(l => l.StdMstId).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.CreatedAt).First());

            var studentIds = links.Keys.Concat(apps.Select(a => a.StdMstId)).Distinct().ToList();
            var students = await _context.StudentMasters.AsNoTracking().Where(s => studentIds.Contains(s.StdMstId)).ToListAsync();

            var rows = students.Select(s =>
            {
                // The current application wins over cancelled/refunded ones.
                var app = apps.Where(a => a.StdMstId == s.StdMstId)
                    .OrderBy(a => a.Status is ExamApplicationStatuses.Cancelled or ExamApplicationStatuses.Refunded ? 1 : 0)
                    .ThenByDescending(a => a.CreatedAt).FirstOrDefault();
                links.TryGetValue(s.StdMstId, out var link);
                var row = new ExamApplicationRow
                {
                    StdMstId = s.StdMstId,
                    StudentId = s.StudentId,
                    Name = StudentNames.Display(s),
                    Email = s.Email,
                    Category = s.Category,
                    LinkState = link == null ? "Not sent"
                        : link.Status == TaskLinkStatuses.Revoked ? "Stopped"
                        : link.OpenedAt != null ? "Opened"
                        : link.SentAt != null ? "Sent" : "Queued",
                    LinkSentAt = Utc(link?.SentAt),
                };
                if (app != null) Fill(row, app, lastOrders.TryGetValue(app.Id, out var st) ? st : null);
                return row;
            }).OrderBy(r => r.StudentId).ToList();

            var (account, _) = await _accounts.ForCollegeAsync(collegeId);
            return new ExamApplicationList
            {
                ExamName = ExamFeeService.DisplayName(exam),
                Kind = kind,
                KindLabel = ExamApplicationKinds.Label(kind),
                FeesSet = setup != null,
                ClosesAt = Utc(setup?.ClosesAt),
                OnlinePaymentReady = account != null && _accounts.Gateway(account.Provider) != null,
                LinksSent = links.Count,
                Applied = rows.Count(r => r.Status != null && r.Status != ExamApplicationStatuses.Cancelled),
                Paid = rows.Count(r => r.Status != null && ExamApplicationStatuses.IsPaid(r.Status)),
                Assigned = rows.Count(r => r.Status == ExamApplicationStatuses.Assigned),
                NeedAttention = rows.Count(r => r.Status == ExamApplicationStatuses.AssignFailed || r.PaidTwice),
                Collected = rows.Where(r => r.Status != null && ExamApplicationStatuses.IsPaid(r.Status)).Sum(r => r.Amount ?? 0),
                Rows = rows,
            };
        }

        private static void Fill(ExamApplicationRow row, ExamApplication app, string? lastOrderStatus)
        {
            row.ApplicationId = app.Id;
            row.Status = app.Status;
            row.Subjects = app.Subjects.Count;
            row.Amount = app.AmountDue;
            row.ReceiptNo = app.ReceiptNo;
            row.PaidAt = Utc(app.PaidAt);
            row.PaidVia = app.PaidVia;
            row.PaymentReference = app.PaymentReference;
            row.LastPaymentStatus = lastOrderStatus;
            row.AssignError = app.AssignError;
            row.PaidTwice = app.PaidTwice;
            row.Note = app.Note;
        }

        private async Task<ExamApplicationRow> RowAsync(Guid applicationId)
        {
            var app = await _context.ExamApplications.AsNoTracking().Include(a => a.Subjects).Include(a => a.Student)
                .FirstOrDefaultAsync(a => a.Id == applicationId) ?? throw new KeyNotFoundException("Application not found.");
            var last = await _context.PaymentOrders.AsNoTracking().Where(o => o.ApplicationId == app.Id)
                .OrderByDescending(o => o.CreatedAt).Select(o => o.Status).FirstOrDefaultAsync();
            var row = new ExamApplicationRow
            {
                StdMstId = app.StdMstId,
                StudentId = app.Student?.StudentId ?? "",
                Name = app.Student == null ? "" : StudentNames.Display(app.Student),
                Email = app.Student?.Email,
                Category = app.Student?.Category,
            };
            Fill(row, app, last);
            return row;
        }

        /// <summary>Loads an application of the signed-in college (the tenant filter) for a change.</summary>
        private async Task<ExamApplication> OwnAsync(Guid applicationId)
        {
            _ = CollegeId;
            return await _context.ExamApplications.FirstOrDefaultAsync(a => a.Id == applicationId)
                ?? throw new KeyNotFoundException("Application not found.");
        }

        public async Task<ExamApplicationRow> PayAtOfficeAsync(Guid applicationId, string reference)
        {
            await OwnAsync(applicationId);
            var app = await _payments.PayAtOfficeAsync(applicationId, reference);
            Audit(app.Id, "ExamApplicationPaidAtOffice", $"Paid at the office: ₹{FeeCalculator.Money(app.AmountDue)}, reference {app.PaymentReference}, receipt {app.ReceiptNo}.");
            await _context.SaveChangesAsync();
            return await RowAsync(applicationId);
        }

        public async Task<ExamApplicationRow> RetryAssignAsync(Guid applicationId)
        {
            var app = await OwnAsync(applicationId);
            if (app.Status is not (ExamApplicationStatuses.Paid or ExamApplicationStatuses.AssignFailed))
                throw new InvalidOperationException(app.Status == ExamApplicationStatuses.Assigned ? "This student is already in the exam." : "Only a paid application can be put into the exam.");
            await _assigner.AssignAsync(applicationId);
            _context.ChangeTracker.Clear();
            return await RowAsync(applicationId);
        }

        public async Task<ExamApplicationRow> CancelAsync(Guid applicationId, string reason)
        {
            var app = await OwnAsync(applicationId);
            var why = (reason ?? "").Trim();
            if (why == "") throw new InvalidOperationException("Say why the application is cancelled.");
            if (app.Status != ExamApplicationStatuses.Draft)
                throw new InvalidOperationException("Only an unpaid application can be cancelled. For a paid one, remove the student in ATKT/Reval Exam and refund the fee.");
            app.Status = ExamApplicationStatuses.Cancelled;
            app.Note = Append(app.Note, $"Cancelled by staff: {why}");
            app.ConcurrencyStamp = Guid.NewGuid();
            Audit(app.Id, "ExamApplicationCancelled", $"Cancelled: {why}");
            await _context.SaveChangesAsync();
            return await RowAsync(applicationId);
        }

        public async Task<ExamApplicationRow> RefundDoneAsync(Guid applicationId, string note)
        {
            var app = await OwnAsync(applicationId);
            var text = (note ?? "").Trim();
            if (text == "") throw new InvalidOperationException("Note the refund (date, amount, reference).");
            if (!app.PaidTwice) throw new InvalidOperationException("This application has no extra payment to refund.");
            app.PaidTwice = false;
            app.Note = Append(app.Note, $"Refund done: {text}");
            app.ConcurrencyStamp = Guid.NewGuid();
            Audit(app.Id, "ExamApplicationRefunded", $"Extra payment refunded: {text}");
            await _context.SaveChangesAsync();
            return await RowAsync(applicationId);
        }

        public async Task<(byte[] Pdf, string FileName)> ReceiptAsync(Guid applicationId)
        {
            var app = await OwnAsync(applicationId);
            if (!ExamApplicationStatuses.IsPaid(app.Status)) throw new InvalidOperationException("The receipt is ready once the fee is paid.");
            return (await ExamReceiptPdf.BuildAsync(_context, app.Id), $"Receipt-{(app.ReceiptNo ?? "exam").Replace('/', '-')}.pdf");
        }

        public async Task<FeeReport> ReportAsync(FeeReportFilter filter)
        {
            _ = CollegeId;
            var q = _context.ExamApplications.AsNoTracking()
                .Where(a => a.PaidAt != null && (a.Status == ExamApplicationStatuses.Paid || a.Status == ExamApplicationStatuses.Assigned
                                                 || a.Status == ExamApplicationStatuses.AssignFailed || a.Status == ExamApplicationStatuses.Refunded));
            if (filter.From is { } from) { var f = from.UtcDateTime; q = q.Where(a => a.PaidAt >= f); }
            if (filter.To is { } to) { var t = to.UtcDateTime; q = q.Where(a => a.PaidAt < t); }
            if (filter.ExamId is Guid examId && examId != Guid.Empty) q = q.Where(a => a.TargetExamId == examId);
            if (!string.IsNullOrWhiteSpace(filter.Kind)) q = q.Where(a => a.Kind == filter.Kind);
            var apps = await q.Include(a => a.Student).Include(a => a.TargetExam).Include(a => a.Subjects)
                .OrderBy(a => a.PaidAt).Take(20000).ToListAsync();
            var ids = apps.Select(a => a.Id).ToList();
            var orders = (await _context.PaymentOrders.AsNoTracking()
                    .Where(o => ids.Contains(o.ApplicationId) && o.Status == PaymentOrderStatuses.Success)
                    .Select(o => new { o.ApplicationId, o.OrderNo, o.GatewayTxnId, o.ConfirmedAt }).ToListAsync())
                .GroupBy(o => o.ApplicationId).ToDictionary(g => g.Key, g => g.OrderBy(o => o.ConfirmedAt).First());

            var rows = apps.Select(a =>
            {
                orders.TryGetValue(a.Id, out var o);
                return new FeeReportRow
                {
                    ReceiptNo = a.ReceiptNo ?? "",
                    PaidAt = DateTime.SpecifyKind(a.PaidAt!.Value, DateTimeKind.Utc),
                    StudentId = a.Student?.StudentId ?? "",
                    Name = a.Student == null ? "" : StudentNames.Display(a.Student),
                    Exam = a.TargetExam == null ? "" : ExamFeeService.DisplayName(a.TargetExam),
                    KindLabel = ExamApplicationKinds.Label(a.Kind),
                    Semester = a.SemesterId,
                    Subjects = a.Subjects.Count,
                    Amount = a.AmountDue,
                    PaidVia = a.PaidVia ?? "",
                    Reference = a.PaidVia == PaidVia.Online ? o?.OrderNo : a.PaymentReference,
                    GatewayTxnId = o?.GatewayTxnId,
                    Status = a.Status,
                };
            }).ToList();
            var counted = rows.Where(r => r.Status != ExamApplicationStatuses.Refunded).ToList();
            return new FeeReport
            {
                Rows = rows,
                Total = counted.Sum(r => r.Amount),
                Online = counted.Where(r => r.PaidVia == PaidVia.Online).Sum(r => r.Amount),
                Office = counted.Where(r => r.PaidVia == PaidVia.Office).Sum(r => r.Amount),
                Waived = counted.Count(r => r.PaidVia == PaidVia.Waived),
            };
        }

        public async Task<(byte[] Content, string FileName)> ReportExcelAsync(FeeReportFilter filter)
        {
            var report = await ReportAsync(filter);
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Fee report");
            var headers = new[] { "Receipt no.", "Paid on", "Student ID", "Name", "Exam", "Kind", "Semester", "Subjects", "Amount (₹)", "Paid", "Order / reference", "Gateway transaction", "Status" };
            for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
            ws.Row(1).Style.Font.Bold = true;
            var r = 2;
            foreach (var row in report.Rows)
            {
                ws.Cell(r, 1).Value = row.ReceiptNo;
                ws.Cell(r, 2).Value = PaymentService.Ist(row.PaidAt);
                ws.Cell(r, 3).Value = row.StudentId;
                ws.Cell(r, 4).Value = row.Name;
                ws.Cell(r, 5).Value = row.Exam;
                ws.Cell(r, 6).Value = row.KindLabel;
                ws.Cell(r, 7).Value = row.Semester ?? "";
                ws.Cell(r, 8).Value = row.Subjects;
                ws.Cell(r, 9).Value = row.Amount;
                ws.Cell(r, 10).Value = row.PaidVia;
                ws.Cell(r, 11).Value = row.Reference ?? "";
                ws.Cell(r, 12).Value = row.GatewayTxnId ?? "";
                ws.Cell(r, 13).Value = row.Status;
                r++;
            }
            ws.Cell(r + 1, 8).Value = "Total";
            ws.Cell(r + 1, 8).Style.Font.Bold = true;
            ws.Cell(r + 1, 9).Value = report.Total;
            ws.Cell(r + 1, 9).Style.Font.Bold = true;
            ws.Column(9).Style.NumberFormat.Format = "#,##0.00";
            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return (ms.ToArray(), $"Exam-fee-report-{DateTime.Now:yyyy-MM-dd}.xlsx");
        }

        private static string Append(string? note, string line)
        {
            var text = string.IsNullOrWhiteSpace(note) ? line : note.Trim() + "\n" + line;
            return text.Length > 1000 ? text[^1000..] : text;
        }

        private static DateTime? Utc(DateTime? value) => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

        private void Audit(Guid applicationId, string evt, string details) =>
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CollegeId = _context.CurrentCollegeId,
                Action = "UPDATE",
                TableName = nameof(ExamApplication),
                RecordId = applicationId.ToString(),
                PerformedBy = _context.CurrentUserId,
                UserType = "Staff",
                Event = evt,
                Details = details.Length > 1000 ? details[..1000] : details,
            });
    }
}
