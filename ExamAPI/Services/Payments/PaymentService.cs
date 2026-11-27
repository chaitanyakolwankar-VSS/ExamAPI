using System.Security.Cryptography;
using System.Text.Json;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using ExamAPI.Services.ExamApplications;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Payments
{
    public class StartPaymentResult
    {
        /// <summary>Nothing to pay (already paid, or the fee is nil): no redirect.</summary>
        public bool Paid { get; set; }
        public string? OrderNo { get; set; }
        public decimal Amount { get; set; }
        public PaymentRedirect? Redirect { get; set; }
    }

    public interface IPaymentService
    {
        /// <summary>
        /// Works out the fee of a draft application and starts paying it. The caller (the student page) has already
        /// checked that the application is the signed-in student's own. Before a new order, open ones are re-checked, so a
        /// second click on "Pay" cannot charge twice for one that just went through.
        /// </summary>
        Task<StartPaymentResult> StartAsync(Guid applicationId, string apiBaseUrl, string returnBaseUrl, CancellationToken ct = default);

        /// <summary>Asks the gateway what happened to an order and records it. Safe to call any number of times, by anyone.</summary>
        Task<PaymentOrder> ConfirmAsync(string orderNo, CancellationToken ct = default);

        /// <summary>Background: re-checks open orders that are due. Answers how many were checked.</summary>
        Task<int> RecheckDueAsync(int batch, CancellationToken ct = default);

        /// <summary>Where the browser goes after the gateway: the student site's result page for this order.</summary>
        string ResultUrl(PaymentOrder order);

        /// <summary>Staff: a draft application paid at the college office (reference = the office receipt). Then assigned.</summary>
        Task<ExamApplication> PayAtOfficeAsync(Guid applicationId, string reference, CancellationToken ct = default);

        /// <summary>What this application costs now (its subjects, the student's category, the late fee if due).</summary>
        Task<FeeBreakdown> FeeAsync(ExamApplication app, CancellationToken ct = default);
    }

    /// <summary>
    /// The payment rules (docs/STUDENT_EXAM_APPLICATIONS.md §4): the server works out the amount; a payment counts only
    /// when the gateway itself confirms this order number at this amount; confirming twice changes nothing; a second
    /// successful payment of one application is flagged for a refund, never counted twice.
    /// Runs without a signed-in college (gateway return, background), so every read ignores the tenant filter and
    /// carries the college of the row it starts from.
    /// </summary>
    public sealed class PaymentService : IPaymentService
    {
        /// <summary>Open orders are re-checked this long, then expire.</summary>
        public static readonly TimeSpan RecheckWindow = TimeSpan.FromHours(48);
        /// <summary>An order the gateway has never heard of after this long: the student never got there.</summary>
        public static readonly TimeSpan NotFoundGiveUp = TimeSpan.FromHours(2);

        private readonly ApplicationDbContext _context;
        private readonly IPaymentAccountResolver _accounts;
        private readonly TimeProvider _clock;
        private readonly ILogger<PaymentService>? _logger;
        private readonly IExamApplicationAssigner? _assigner;
        private readonly IEmailOutbox? _outbox;

        public PaymentService(ApplicationDbContext context, IPaymentAccountResolver accounts, TimeProvider? clock = null,
            ILogger<PaymentService>? logger = null, IExamApplicationAssigner? assigner = null, IEmailOutbox? outbox = null)
        {
            _context = context;
            _accounts = accounts;
            _clock = clock ?? TimeProvider.System;
            _logger = logger;
            _assigner = assigner;
            _outbox = outbox;
        }

        /// <summary>Applications marked paid in this unit of work: put into the exam once it is saved.</summary>
        private readonly List<Guid> _justPaid = new();

        private async Task AssignJustPaidAsync(CancellationToken ct)
        {
            var ids = _justPaid.ToList();
            _justPaid.Clear();
            if (_assigner == null) return;
            foreach (var id in ids) await _assigner.AssignAsync(id, ct);
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        public string ResultUrl(PaymentOrder order) =>
            (order.ReturnBaseUrl ?? "").TrimEnd('/') + "/student?payment=" + Uri.EscapeDataString(order.OrderNo);

        // =====================================================================
        // Start
        // =====================================================================

        public async Task<StartPaymentResult> StartAsync(Guid applicationId, string apiBaseUrl, string returnBaseUrl, CancellationToken ct = default)
        {
            // A click on "Pay" after an earlier attempt: settle the open orders first.
            var openOrders = await _context.PaymentOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.ApplicationId == applicationId
                            && (o.Status == PaymentOrderStatuses.Created || o.Status == PaymentOrderStatuses.Pending))
                .Select(o => o.OrderNo)
                .ToListAsync(ct);
            foreach (var no in openOrders) await ConfirmAsync(no, ct);
            _context.ChangeTracker.Clear();

            var app = await LoadApplicationAsync(applicationId, ct);
            if (ExamApplicationStatuses.IsPaid(app.Status))
                return new StartPaymentResult { Paid = true, Amount = app.AmountDue };
            if (app.Status != ExamApplicationStatuses.Draft)
                throw new InvalidOperationException("This application was cancelled. Please contact the exam office.");
            if (app.Subjects.Count == 0)
                throw new InvalidOperationException("Choose at least one subject.");

            var setup = await _context.ExamFeeSetups.IgnoreQueryFilters()
                .Include(s => s.CategoryRates)
                .FirstOrDefaultAsync(s => s.ExamId == app.TargetExamId && s.CollegeId == app.CollegeId && !s.IsDeleted, ct)
                ?? throw new InvalidOperationException("The fee for this exam has not been set yet. Please contact the exam office.");
            var now = Now;
            if (setup.OpensAt is { } opens && now < opens)
                throw new InvalidOperationException($"Applications for this exam open on {Ist(opens)}.");
            if (now >= setup.ClosesAt)
                throw new InvalidOperationException($"Applications for this exam closed on {Ist(setup.ClosesAt)}.");

            var fee = FeeCalculator.Compute(setup, app.Student?.Category, app.Subjects.Count, now);
            app.AmountDue = fee.Total;
            app.FeeBreakdownJson = JsonSerializer.Serialize(fee);
            app.ConcurrencyStamp = Guid.NewGuid();

            if (fee.Total == 0)
            {
                await MarkPaidAsync(app, PaidVia.Waived, now, ct);
                await _context.SaveChangesAsync(ct);
                await AssignJustPaidAsync(ct);
                return new StartPaymentResult { Paid = true, Amount = 0 };
            }

            var collegeId = app.CollegeId ?? throw new InvalidOperationException("The application has no college.");
            var (account, _) = await _accounts.ForCollegeAsync(collegeId);
            var gateway = account == null ? null : _accounts.Gateway(account.Provider);
            if (account == null || gateway == null)
                throw new InvalidOperationException("Online payment isn't set up for this college yet. Please contact the exam office.");

            var order = new PaymentOrder
            {
                Id = Guid.NewGuid(),
                CollegeId = collegeId,
                ApplicationId = app.Id,
                OrderNo = NewOrderNo(),
                Amount = fee.Total,
                PaymentAccountId = account.Id,
                Provider = account.Provider,
                IsTestMode = account.IsTestMode,
                Status = PaymentOrderStatuses.Created,
                ReturnBaseUrl = returnBaseUrl.TrimEnd('/'),
                CreatedAt = now,
                // Checked even when the browser never comes back.
                NextCheckAt = now.AddMinutes(10),
            };
            _context.PaymentOrders.Add(order);
            await _context.SaveChangesAsync(ct);

            var apiBase = apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/";
            var student = app.Student;
            try
            {
                var redirect = await gateway.StartAsync(_accounts.Open(account), new PaymentStartRequest(
                    order.OrderNo, order.Amount, order.Currency,
                    $"{ExamApplicationKinds.Label(app.Kind)} fee: {app.TargetExam?.Name}",
                    student == null ? "Student" : ExamAPI.Services.Common.StudentNames.Display(student),
                    student?.Email, student?.Phone,
                    apiBase + "api/Payments/return/" + Uri.EscapeDataString(order.OrderNo),
                    apiBase), ct);
                return new StartPaymentResult { OrderNo = order.OrderNo, Amount = order.Amount, Redirect = redirect };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Payment start failed for order {OrderNo}", order.OrderNo);
                order.Status = PaymentOrderStatuses.Failed;
                order.Message = "The payment page could not be opened.";
                order.NextCheckAt = null;
                order.ConcurrencyStamp = Guid.NewGuid();
                await _context.SaveChangesAsync(ct);
                throw new InvalidOperationException("The payment page could not be opened. Please try again in a few minutes.");
            }
        }

        // =====================================================================
        // Confirm
        // =====================================================================

        public async Task<PaymentOrder> ConfirmAsync(string orderNo, CancellationToken ct = default)
        {
            for (var attempt = 0; ; attempt++)
            {
                _context.ChangeTracker.Clear();
                _justPaid.Clear();
                try
                {
                    var order = await ConfirmOnceAsync(orderNo, ct);
                    await AssignJustPaidAsync(ct);
                    return order;
                }
                catch (DbUpdateConcurrencyException) when (attempt < 3)
                {
                    // Someone else (the browser's return, the background check) saved first: read again and decide again.
                }
                catch (DbUpdateException ex) when (attempt < 3 && IsReceiptClash(ex))
                {
                }
            }
        }

        private async Task<PaymentOrder> ConfirmOnceAsync(string orderNo, CancellationToken ct)
        {
            var order = await _context.PaymentOrders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderNo == orderNo, ct)
                ?? throw new KeyNotFoundException("Payment not found.");
            if (order.Status is PaymentOrderStatuses.Success or PaymentOrderStatuses.Failed or PaymentOrderStatuses.Mismatch)
                return order;

            var now = Now;
            var account = order.PaymentAccountId == null ? null : await _accounts.ByIdAsync(order.PaymentAccountId.Value);
            var gateway = _accounts.Gateway(order.Provider);
            if (account == null || gateway == null)
            {
                order.Message = "The gateway account of this payment is not available on this server.";
                Schedule(order, now);
                await SaveOrderAsync(order, ct);
                return order;
            }

            GatewayStatus status;
            try
            {
                status = await gateway.GetStatusAsync(_accounts.Open(account), order.OrderNo, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Payment status check failed for order {OrderNo}", order.OrderNo);
                order.LastCheckedAt = now;
                order.CheckCount++;
                order.Message = "The gateway did not answer; checking again shortly.";
                Schedule(order, now);
                await SaveOrderAsync(order, ct);
                return order;
            }

            order.LastCheckedAt = now;
            order.CheckCount++;
            order.GatewayStatus = Cut(status.RawStatus, 100);
            order.GatewayTxnId = Cut(status.TxnId, 100) ?? order.GatewayTxnId;
            order.Mode = Cut(status.Mode, 50) ?? order.Mode;
            order.Bank = Cut(status.Bank, 100) ?? order.Bank;

            switch (status.State)
            {
                case GatewayState.Success:
                    if (!string.Equals(status.OrderNo, order.OrderNo, StringComparison.Ordinal) || status.Amount != order.Amount)
                    {
                        // Never counted: staff compare it with the gateway's records.
                        order.Status = PaymentOrderStatuses.Mismatch;
                        order.NextCheckAt = null;
                        order.Message = $"The gateway reported ₹{FeeCalculator.Money(status.Amount ?? 0)} for order {status.OrderNo}; "
                                        + $"this order is ₹{FeeCalculator.Money(order.Amount)}. Not counted: check with the gateway.";
                        _logger?.LogWarning("Payment mismatch on order {OrderNo}", order.OrderNo);
                        break;
                    }
                    order.Status = PaymentOrderStatuses.Success;
                    order.ConfirmedAt = now;
                    order.NextCheckAt = null;
                    order.Message = null;
                    var app = await LoadApplicationAsync(order.ApplicationId, ct);
                    if (ExamApplicationStatuses.IsPaid(app.Status) || app.Status == ExamApplicationStatuses.Refunded)
                    {
                        app.PaidTwice = true;
                        app.Note = Append(app.Note, $"Paid twice: order {order.OrderNo} also succeeded on {Ist(now)}; refund one payment.");
                        app.ConcurrencyStamp = Guid.NewGuid();
                    }
                    else if (app.Status == ExamApplicationStatuses.Cancelled)
                    {
                        app.Note = Append(app.Note, $"Paid after cancelling: order {order.OrderNo} on {Ist(now)}; refund or restore.");
                        app.PaidTwice = true;
                        app.ConcurrencyStamp = Guid.NewGuid();
                    }
                    else
                    {
                        await MarkPaidAsync(app, PaidVia.Online, now, ct);
                    }
                    break;

                case GatewayState.Failed:
                    order.Status = PaymentOrderStatuses.Failed;
                    order.NextCheckAt = null;
                    order.Message = Cut(status.Message, 500) ?? "The payment did not go through.";
                    break;

                case GatewayState.NotFound when now - order.CreatedAt >= NotFoundGiveUp:
                    order.Status = PaymentOrderStatuses.Expired;
                    order.NextCheckAt = null;
                    order.Message = "The payment was not started at the gateway.";
                    break;

                default: // Pending, or NotFound while the student may still be on the gateway's page
                    order.Status = order.Status == PaymentOrderStatuses.Expired ? PaymentOrderStatuses.Expired : PaymentOrderStatuses.Pending;
                    order.Message = Cut(status.Message, 500);
                    Schedule(order, now);
                    break;
            }

            await SaveOrderAsync(order, ct);
            return order;
        }

        public async Task<int> RecheckDueAsync(int batch, CancellationToken ct = default)
        {
            var now = Now;
            var due = await _context.PaymentOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => (o.Status == PaymentOrderStatuses.Created || o.Status == PaymentOrderStatuses.Pending)
                            && o.NextCheckAt != null && o.NextCheckAt <= now)
                .OrderBy(o => o.NextCheckAt)
                .Select(o => o.OrderNo)
                .Take(batch)
                .ToListAsync(ct);
            foreach (var no in due)
            {
                try
                {
                    await ConfirmAsync(no, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogError(ex, "Payment re-check failed for order {OrderNo}", no);
                }
            }

            // Paid but not put into the exam (the server stopped in between): try again. AssignFailed waits for staff.
            var stale = new List<Guid>();
            if (_assigner != null)
            {
                var cutoff = now.AddMinutes(-2);
                stale = await _context.ExamApplications.IgnoreQueryFilters().AsNoTracking()
                    .Where(a => !a.IsDeleted && a.Status == ExamApplicationStatuses.Paid && a.PaidAt != null && a.PaidAt < cutoff)
                    .OrderBy(a => a.PaidAt)
                    .Select(a => a.Id)
                    .Take(batch)
                    .ToListAsync(ct);
                foreach (var id in stale) await _assigner.AssignAsync(id, ct);
            }
            return Math.Max(due.Count, stale.Count);
        }

        public async Task<ExamApplication> PayAtOfficeAsync(Guid applicationId, string reference, CancellationToken ct = default)
        {
            var refText = (reference ?? "").Trim();
            if (refText == "") throw new InvalidOperationException("Enter the office receipt number or reference.");
            if (refText.Length > 100) throw new InvalidOperationException("The reference can be at most 100 characters.");
            _context.ChangeTracker.Clear();
            _justPaid.Clear();
            var app = await LoadApplicationAsync(applicationId, ct);
            if (app.Status != ExamApplicationStatuses.Draft)
                throw new InvalidOperationException(ExamApplicationStatuses.IsPaid(app.Status) ? "This application is already paid." : "This application was cancelled.");
            if (app.Subjects.Count == 0) throw new InvalidOperationException("The application has no subjects.");
            // The office may take a payment after the closing date: the window is not checked here.
            var fee = await FeeAsync(app, ct);
            app.AmountDue = fee.Total;
            app.FeeBreakdownJson = JsonSerializer.Serialize(fee);
            app.PaymentReference = refText;
            await MarkPaidAsync(app, fee.Total == 0 ? PaidVia.Waived : PaidVia.Office, Now, ct);
            await _context.SaveChangesAsync(ct);
            await AssignJustPaidAsync(ct);
            return await LoadApplicationAsync(applicationId, ct);
        }

        public async Task<FeeBreakdown> FeeAsync(ExamApplication app, CancellationToken ct = default)
        {
            var setup = await _context.ExamFeeSetups.IgnoreQueryFilters()
                .Include(s => s.CategoryRates)
                .FirstOrDefaultAsync(s => s.ExamId == app.TargetExamId && s.CollegeId == app.CollegeId && !s.IsDeleted, ct)
                ?? throw new InvalidOperationException("The fee for this exam has not been set yet. Please contact the exam office.");
            var category = app.Student?.Category
                ?? await _context.StudentMasters.IgnoreQueryFilters().Where(s => s.StdMstId == app.StdMstId).Select(s => s.Category).FirstOrDefaultAsync(ct);
            return FeeCalculator.Compute(setup, category, app.Subjects.Count, Now);
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>Paid: status, time, how, and the next receipt number of its series.</summary>
        public async Task MarkPaidAsync(ExamApplication app, string via, DateTime at, CancellationToken ct = default)
        {
            app.Status = ExamApplicationStatuses.Paid;
            app.PaidAt = at;
            app.PaidVia = via;
            app.ReceiptNo ??= await NextReceiptNoAsync(app, ct);
            app.ConcurrencyStamp = Guid.NewGuid();
            _justPaid.Add(app.Id);

            // The link counts as used (staff see "paid"); it stays open for the receipt until the closing date.
            if (app.TaskLinkId != null)
            {
                var link = await _context.TaskLinks.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == app.TaskLinkId, ct);
                if (link != null) link.UsedAt ??= at;
            }
            QueueReceiptEmail(app, via);
        }

        /// <summary>"Payment received" to the student (the receipt PDF stays on their link).</summary>
        private void QueueReceiptEmail(ExamApplication app, string via)
        {
            var to = app.Student?.Email;
            if (_outbox == null || app.CollegeId == null || string.IsNullOrWhiteSpace(to)) return;
            var name = string.IsNullOrWhiteSpace(app.Student!.FirstName) ? "Student" : app.Student.FirstName.Trim();
            var exam = $"{app.TargetExam?.Name} ({ExamApplicationKinds.Label(app.Kind)})";
            var what = via == PaidVia.Waived ? "Your application was accepted (no fee for your category)" : $"We received your payment of ₹{FeeCalculator.Money(app.AmountDue)}";
            var e = (Func<string, string>)System.Net.WebUtility.HtmlEncode;
            var html = $"<p>Dear {e(name)},</p><p>{e(what)} for <b>{e(exam)}</b>.</p>"
                       + $"<p>Receipt number: <b>{e(app.ReceiptNo ?? "")}</b></p>"
                       + "<p>You can download the receipt and your application from the same link you applied with, until applications close.</p>";
            var text = $"Dear {name},\n\n{what} for {exam}.\n\nReceipt number: {app.ReceiptNo}\n\n"
                       + "You can download the receipt and your application from the same link you applied with, until applications close.";
            _outbox.Enqueue(app.CollegeId.Value, EmailKinds.Plain, app.Id, to!, EmailPriorities.High,
                $"Receipt {app.ReceiptNo}: {exam}", html, text);
        }

        /// <summary>e.g. "AT/2026-27/00012": per college, kind and academic year. The counter is checked on save, so two payments never get one number.</summary>
        private async Task<string> NextReceiptNoAsync(ExamApplication app, CancellationToken ct)
        {
            var collegeId = app.CollegeId!.Value;
            var ayid = app.TargetExam?.AcademicYearAYID;
            var ay = ayid == null ? null : await _context.AcademicYears.IgnoreQueryFilters()
                .Where(a => a.AYID == ayid).Select(a => a.ShortDuration ?? a.FullDuration).FirstOrDefaultAsync(ct);
            var prefix = app.Kind switch
            {
                ExamApplicationKinds.Atkt => "AT",
                ExamApplicationKinds.ReExam => "RE",
                ExamApplicationKinds.Revaluation => "RV",
                _ => "EX",
            };
            var series = $"{prefix}/{(string.IsNullOrWhiteSpace(ay) ? Now.Year.ToString() : ay.Trim())}";
            if (series.Length > 40) series = series[..40];

            var counter = await _context.ReceiptCounters.FirstOrDefaultAsync(c => c.CollegeId == collegeId && c.Series == series, ct);
            if (counter == null)
            {
                counter = new ReceiptCounter { CollegeId = collegeId, Series = series, LastNo = 0 };
                _context.ReceiptCounters.Add(counter);
            }
            counter.LastNo++;
            return $"{series}/{counter.LastNo:D5}";
        }

        private async Task<ExamApplication> LoadApplicationAsync(Guid applicationId, CancellationToken ct) =>
            await _context.ExamApplications.IgnoreQueryFilters()
                .Include(a => a.Subjects)
                .Include(a => a.Student)
                .Include(a => a.TargetExam)
                .FirstOrDefaultAsync(a => a.Id == applicationId && !a.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Application not found.");

        private async Task SaveOrderAsync(PaymentOrder order, CancellationToken ct)
        {
            order.ConcurrencyStamp = Guid.NewGuid();
            await _context.SaveChangesAsync(ct);
        }

        /// <summary>Next check: every 5 minutes in the first hour, then every 30; expired after the window.</summary>
        private static void Schedule(PaymentOrder order, DateTime now)
        {
            if (now - order.CreatedAt >= RecheckWindow)
            {
                order.Status = PaymentOrderStatuses.Expired;
                order.NextCheckAt = null;
                order.Message ??= "No answer from the gateway within 48 hours.";
                return;
            }
            order.NextCheckAt = now.AddMinutes(now - order.CreatedAt < TimeSpan.FromHours(1) ? 5 : 30);
        }

        /// <summary>"GS" + 16 random letters/digits (no 0/O/1/I): unguessable, and fits every gateway's order-id rules.</summary>
        public static string NewOrderNo()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            Span<char> chars = stackalloc char[16];
            for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            return "GS" + new string(chars);
        }

        private static bool IsReceiptClash(DbUpdateException ex) =>
            ex.InnerException?.Message.Contains("ReceiptCounter", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) == true;

        private static string? Cut(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();

        private static string Append(string? note, string line)
        {
            var text = string.IsNullOrWhiteSpace(note) ? line : note.Trim() + "\n" + line;
            return text.Length > 1000 ? text[^1000..] : text;
        }

        private static readonly TimeZoneInfo India = FindIndia();
        private static TimeZoneInfo FindIndia()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
            catch (TimeZoneNotFoundException)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
                catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST"); }
            }
        }

        /// <summary>A UTC time as the exam office reads it.</summary>
        public static string Ist(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), India).ToString("d MMM yyyy, h:mm tt");
    }
}
