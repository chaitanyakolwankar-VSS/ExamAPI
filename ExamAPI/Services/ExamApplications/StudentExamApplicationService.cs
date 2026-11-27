using System.Security.Claims;
using System.Text.Json;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.AtktRevalExam;
using ExamAPI.Services.Common;
using ExamAPI.Services.Payments;
using ExamAPI.Services.StudentAccess;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.ExamApplications
{
    public class StudentApplicationSubject
    {
        public Guid SubjectId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Failed, Passed, Absent, NotAttempted (the result of the attempt it is based on).</summary>
        public string Status { get; set; } = "";
        /// <summary>e.g. "34 / 100"; null when there are no marks.</summary>
        public string? Marks { get; set; }
        public bool Selectable { get; set; }
        /// <summary>Ticked to start with (ATKT/Re-Exam: the subjects not cleared).</summary>
        public bool Suggested { get; set; }
        public string? Reason { get; set; }
    }

    public class StudentPaymentInfo
    {
        public string OrderNo { get; set; } = "";
        public string Status { get; set; } = "";
        public decimal Amount { get; set; }
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class StudentApplicationInfo
    {
        public string Status { get; set; } = "";
        public List<Guid> SubjectIds { get; set; } = new();
        public decimal AmountDue { get; set; }
        public FeeBreakdown? Fee { get; set; }
        public string? ReceiptNo { get; set; }
        public DateTime? PaidAt { get; set; }
        public string? PaidVia { get; set; }
        public DateTime? AssignedAt { get; set; }
        public StudentPaymentInfo? LastPayment { get; set; }
    }

    public class StudentApplicationView
    {
        public string CollegeName { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string StudentId { get; set; } = "";
        public string ExamName { get; set; } = "";
        public string Kind { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public string? BasedOn { get; set; }
        public DateTime? OpensAt { get; set; }
        public DateTime? ClosesAt { get; set; }
        /// <summary>Applications and payments are taken now.</summary>
        public bool IsOpen { get; set; }
        public string? Notes { get; set; }
        /// <summary>What the student declares before paying (printed on the form).</summary>
        public string Declaration { get; set; } = "";
        /// <summary>The college's note on the form (e.g. attach previous marksheets).</summary>
        public string? FormNote { get; set; }
        /// <summary>Why the student can't apply (closed, nothing to apply for, ...); null when they can.</summary>
        public string? Message { get; set; }
        public int? MaxSubjects { get; set; }
        public List<StudentApplicationSubject> Subjects { get; set; } = new();
        public StudentApplicationInfo? Application { get; set; }
    }

    public class StudentPayResult
    {
        public bool Paid { get; set; }
        public decimal Amount { get; set; }
        public string? OrderNo { get; set; }
        public PaymentRedirect? Redirect { get; set; }
    }

    public interface IStudentExamApplicationService
    {
        Task<StudentApplicationView> GetAsync(ClaimsPrincipal user);
        /// <summary>What these subjects would cost now (nothing saved).</summary>
        Task<FeeBreakdown> QuoteAsync(ClaimsPrincipal user, List<Guid> subjectIds);
        /// <summary>Saves the chosen subjects and starts the payment (or finishes at once when there is no fee).</summary>
        Task<StudentPayResult> PayAsync(ClaimsPrincipal user, List<Guid> subjectIds, bool declared, string apiBaseUrl, string siteBaseUrl);
        /// <summary>After the gateway: asks it about the student's own open payments (never one named by the browser), then answers the page.</summary>
        Task<StudentApplicationView> PaymentResultAsync(ClaimsPrincipal user);
        /// <summary>The receipt and application form (PDF), once paid.</summary>
        Task<(byte[] Pdf, string FileName)> DocumentAsync(ClaimsPrincipal user, ExamDocumentParts part);
    }

    /// <summary>
    /// The student's side of an exam application link (TaskTypes.AtktPayment): the subjects come from the same rules as the
    /// ATKT/Reval Exam screen (its matrix), never from the browser; the fee only from <see cref="FeeCalculator"/>.
    /// Everything is read for the signed-in link's student, exam and class.
    /// </summary>
    public sealed class StudentExamApplicationService : IStudentExamApplicationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAtktRevalExamService _atkt;
        private readonly IPaymentService _payments;
        private readonly ExamAPI.Services.Files.IFileStorage? _storage;

        public StudentExamApplicationService(ApplicationDbContext context, IAtktRevalExamService atkt, IPaymentService payments,
            ExamAPI.Services.Files.IFileStorage? storage = null)
        {
            _context = context;
            _atkt = atkt;
            _payments = payments;
            _storage = storage;
        }

        private sealed record LinkContext(TaskLink Link, TaskBatch Batch, StudentMaster Student, ExamMaster Exam, string Kind, ExamFeeSetup? Setup);

        private async Task<LinkContext> LoadAsync(ClaimsPrincipal user)
        {
            var linkId = StudentAccessAuth.Read(user, StudentAccessAuth.LinkClaim) ?? throw new UnauthorizedAccessException();
            var link = await _context.TaskLinks.Include(l => l.Batch).Include(l => l.Student).FirstOrDefaultAsync(l => l.Id == linkId)
                ?? throw new UnauthorizedAccessException();
            if (!StudentAccessAuth.IsOpen(link, DateTime.UtcNow)) throw new UnauthorizedAccessException();
            if (link.TaskTypeCode != TaskTypes.AtktPayment || link.Batch?.RefId == null || link.Student == null)
                throw new InvalidOperationException("This link isn't for an exam application.");
            var exam = await _context.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.ExamId == link.Batch.RefId)
                ?? throw new InvalidOperationException("The exam of this link no longer exists. Please contact the exam office.");
            var kind = ExamFeeService.KindOf(exam) ?? throw new InvalidOperationException("Students can't apply for this exam.");
            var setup = await _context.ExamFeeSetups.AsNoTracking().Include(s => s.CategoryRates).FirstOrDefaultAsync(s => s.ExamId == exam.ExamId);
            return new LinkContext(link, link.Batch, link.Student, exam, kind, setup);
        }

        /// <summary>The open (not cancelled or refunded) application of this student for this exam.</summary>
        private Task<ExamApplication?> ApplicationAsync(LinkContext c, bool tracking = false)
        {
            var q = _context.ExamApplications.Include(a => a.Subjects)
                .Where(a => a.StdMstId == c.Student.StdMstId && a.TargetExamId == c.Exam.ExamId
                            && a.Status != ExamApplicationStatuses.Cancelled && a.Status != ExamApplicationStatuses.Refunded);
            return (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync();
        }

        private AtktMatrixRequest Filter(LinkContext c, bool editMode) => new()
        {
            CourseId = c.Exam.CourseId ?? Guid.Empty,
            Ayid = c.Exam.AcademicYearAYID ?? Guid.Empty,
            Semester = c.Batch.SemesterId ?? "",
            Pattern = c.Batch.Pattern ?? "",
            Mode = c.Kind == ExamApplicationKinds.Revaluation ? AssignmentModes.Revaluation : AssignmentModes.Atkt,
            SourceExamId = c.Kind == ExamApplicationKinds.Revaluation ? c.Exam.RevaluationForExamId : null,
            TargetExamId = c.Exam.ExamId,
            EditMode = editMode,
        };

        /// <summary>This student's row of the assignment matrix (what they may apply for), with the matrix's columns.</summary>
        private async Task<(AtktStudentRowDto? Row, AtktMatrixResponseDto Matrix)> RowAsync(LinkContext c, bool editMode = false)
        {
            var matrix = await _atkt.GetMatrixAsync(Filter(c, editMode));
            return (matrix.Students.FirstOrDefault(s => s.StdMstId == c.Student.StdMstId), matrix);
        }

        public async Task<StudentApplicationView> GetAsync(ClaimsPrincipal user)
        {
            var c = await LoadAsync(user);
            return await ViewAsync(c);
        }

        private async Task<StudentApplicationView> ViewAsync(LinkContext c)
        {
            var now = DateTime.UtcNow;
            var view = new StudentApplicationView
            {
                CollegeName = await StudentLinkEmails.CollegeNameAsync(_context, c.Link.CollegeId),
                StudentName = StudentNames.Display(c.Student),
                StudentId = c.Student.StudentId,
                ExamName = ExamFeeService.DisplayName(c.Exam),
                Kind = c.Kind,
                KindLabel = ExamApplicationKinds.Label(c.Kind),
                OpensAt = Utc(c.Setup?.OpensAt),
                ClosesAt = Utc(c.Setup?.ClosesAt),
                Notes = c.Setup?.Notes,
            };
            view.IsOpen = c.Setup != null && (c.Setup.OpensAt == null || now >= c.Setup.OpensAt) && now < c.Setup.ClosesAt;
            var wording = await ExamFormWording.LoadAsync(_context, c.Link.CollegeId);
            view.Declaration = wording.Declaration;
            view.FormNote = wording.Note;

            var app = await ApplicationAsync(c);
            if (app != null) view.Application = await InfoAsync(app);

            var (row, matrix) = await RowAsync(c);
            view.MaxSubjects = matrix.Policy?.MaxSubjectsPerStudent;
            if (row != null)
            {
                view.BasedOn = row.SourceExamName;
                view.Subjects = Subjects(row, matrix);
            }
            else if (app != null)
            {
                // Already put into the exam: the matrix no longer lists them as a candidate. Show what they applied for.
                var (assignedRow, assignedMatrix) = await RowAsync(c, editMode: true);
                if (assignedRow != null) view.Subjects = Subjects(assignedRow, assignedMatrix);
            }

            if (c.Setup == null) view.Message = "Applications for this exam are not open yet. Please check again later.";
            else if (app != null && ExamApplicationStatuses.IsPaid(app.Status)) view.Message = null;
            else if (c.Setup.OpensAt is { } o && now < o) view.Message = $"Applications open on {PaymentService.Ist(o)}.";
            else if (now >= c.Setup.ClosesAt) view.Message = $"Applications closed on {PaymentService.Ist(c.Setup.ClosesAt)}.";
            else if (row == null)
            {
                var (assigned, _) = await RowAsync(c, editMode: true);
                view.Message = assigned != null
                    ? "You are already registered for this exam. Contact the exam office for any change."
                    : !string.IsNullOrEmpty(matrixMessage(matrix)) ? matrixMessage(matrix) : "There is nothing for you to apply for in this exam.";
            }
            else if (!view.Subjects.Any(s => s.Selectable)) view.Message = "There is nothing for you to apply for in this exam.";
            return view;

            static string? matrixMessage(AtktMatrixResponseDto m) => m.Success ? null : m.Message;
        }

        private static List<StudentApplicationSubject> Subjects(AtktStudentRowDto row, AtktMatrixResponseDto matrix) =>
            matrix.Columns.OrderBy(col => col.Order).Select(col =>
            {
                var cell = row.Cells.FirstOrDefault(x => x.SubjectId == col.SubjectId);
                return new StudentApplicationSubject
                {
                    SubjectId = col.SubjectId,
                    Code = col.SubjectCode,
                    Name = col.SubjectName,
                    Status = cell?.Status ?? "",
                    Marks = cell == null || cell.OutOfTotal == 0 || cell.IsAbsent ? null : $"{cell.ObtainedTotal} / {cell.OutOfTotal}",
                    Selectable = cell?.Selectable == true,
                    Suggested = cell?.Selected == true,
                    Reason = cell?.Selectable == true ? null : cell?.Reason,
                };
            }).ToList();

        private async Task<StudentApplicationInfo> InfoAsync(ExamApplication app)
        {
            var last = await _context.PaymentOrders.AsNoTracking()
                .Where(o => o.ApplicationId == app.Id)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();
            return new StudentApplicationInfo
            {
                Status = app.Status,
                SubjectIds = app.Subjects.Select(s => s.SubjectId).ToList(),
                AmountDue = app.AmountDue,
                Fee = string.IsNullOrEmpty(app.FeeBreakdownJson) ? null : JsonSerializer.Deserialize<FeeBreakdown>(app.FeeBreakdownJson),
                ReceiptNo = app.ReceiptNo,
                PaidAt = Utc(app.PaidAt),
                PaidVia = app.PaidVia,
                AssignedAt = Utc(app.AssignedAt),
                LastPayment = last == null ? null : new StudentPaymentInfo
                {
                    OrderNo = last.OrderNo,
                    Status = last.Status,
                    Amount = last.Amount,
                    Message = last.Message,
                    CreatedAt = DateTime.SpecifyKind(last.CreatedAt, DateTimeKind.Utc),
                },
            };
        }

        /// <summary>The chosen subjects, checked against what the rules allow this student (never trusted as sent).</summary>
        private async Task<List<Guid>> CheckSelectionAsync(LinkContext c, List<Guid>? subjectIds)
        {
            var chosen = (subjectIds ?? new()).Distinct().ToList();
            if (chosen.Count == 0) throw new InvalidOperationException("Choose at least one subject.");
            var (row, matrix) = await RowAsync(c);
            if (row == null) throw new InvalidOperationException("There is nothing for you to apply for in this exam.");
            var allowed = row.Cells.Where(x => x.Selectable).Select(x => x.SubjectId).ToHashSet();
            if (chosen.Any(id => !allowed.Contains(id)))
                throw new InvalidOperationException("One of the chosen subjects can't be applied for. Reload the page and choose again.");
            if (matrix.Policy?.MaxSubjectsPerStudent is int max && max > 0 && chosen.Count > max)
                throw new InvalidOperationException($"You can apply for at most {max} subject{(max == 1 ? "" : "s")}.");
            return chosen;
        }

        public async Task<FeeBreakdown> QuoteAsync(ClaimsPrincipal user, List<Guid> subjectIds)
        {
            var c = await LoadAsync(user);
            if (c.Setup == null) throw new InvalidOperationException("Applications for this exam are not open yet.");
            var chosen = await CheckSelectionAsync(c, subjectIds);
            return FeeCalculator.Compute(c.Setup, c.Student.Category, chosen.Count, DateTime.UtcNow);
        }

        public async Task<StudentPayResult> PayAsync(ClaimsPrincipal user, List<Guid> subjectIds, bool declared, string apiBaseUrl, string siteBaseUrl)
        {
            var c = await LoadAsync(user);
            if (c.Setup == null) throw new InvalidOperationException("Applications for this exam are not open yet.");
            var app = await ApplicationAsync(c, tracking: true);
            if (app != null && app.Status != ExamApplicationStatuses.Draft)
                return new StudentPayResult { Paid = true, Amount = app.AmountDue };
            if (!declared) throw new InvalidOperationException("Read and tick the declaration first.");

            var chosen = await CheckSelectionAsync(c, subjectIds);
            if (app == null)
            {
                app = new ExamApplication
                {
                    Id = Guid.NewGuid(),
                    CollegeId = c.Link.CollegeId,
                    StdMstId = c.Student.StdMstId,
                    TargetExamId = c.Exam.ExamId,
                    SourceExamId = c.Kind == ExamApplicationKinds.Revaluation ? c.Exam.RevaluationForExamId : null,
                    TaskLinkId = c.Link.Id,
                    Kind = c.Kind,
                    SemesterId = c.Batch.SemesterId,
                    Pattern = c.Batch.Pattern,
                };
                _context.ExamApplications.Add(app);
            }
            else
            {
                _context.ExamApplicationSubjects.RemoveRange(app.Subjects);
                app.Subjects.Clear();
                app.TaskLinkId = c.Link.Id;
                app.SemesterId = c.Batch.SemesterId;
                app.Pattern = c.Batch.Pattern;
                app.ConcurrencyStamp = Guid.NewGuid();
            }
            foreach (var id in chosen)
                app.Subjects.Add(new ExamApplicationSubject { Id = Guid.NewGuid(), CollegeId = c.Link.CollegeId, ApplicationId = app.Id, SubjectId = id });
            app.DeclaredAt = DateTime.UtcNow;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Two tabs applied at once: the other one's application stands.
                throw new InvalidOperationException("Your application was just changed in another window. Reload the page.");
            }

            var start = await _payments.StartAsync(app.Id, apiBaseUrl, siteBaseUrl);
            return new StudentPayResult { Paid = start.Paid, Amount = start.Amount, OrderNo = start.OrderNo, Redirect = start.Redirect };
        }

        public async Task<StudentApplicationView> PaymentResultAsync(ClaimsPrincipal user)
        {
            var c = await LoadAsync(user);
            var app = await ApplicationAsync(c);
            var open = app == null ? new List<string>() : await _context.PaymentOrders
                .Where(o => o.ApplicationId == app.Id && (o.Status == PaymentOrderStatuses.Created || o.Status == PaymentOrderStatuses.Pending))
                .Select(o => o.OrderNo).ToListAsync();
            foreach (var no in open) await _payments.ConfirmAsync(no);
            _context.ChangeTracker.Clear();
            return await ViewAsync(c);
        }

        public async Task<(byte[] Pdf, string FileName)> DocumentAsync(ClaimsPrincipal user, ExamDocumentParts part)
        {
            var c = await LoadAsync(user);
            var app = await ApplicationAsync(c);
            if (app == null || !ExamApplicationStatuses.IsPaid(app.Status))
                throw new InvalidOperationException("The application form and receipt are ready once the fee is paid.");
            var pdf = await ExamReceiptPdf.BuildAsync(_context, _storage, app.Id, part);
            var what = part == ExamDocumentParts.Form ? "Application-form" : part == ExamDocumentParts.Receipt ? "Receipt" : "Application-and-receipt";
            return (pdf, $"{what}-{(app.ReceiptNo ?? "exam").Replace('/', '-')}.pdf");
        }

        private static DateTime? Utc(DateTime? value) => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }
}
