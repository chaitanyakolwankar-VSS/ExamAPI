using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.ExamApplications
{
    public class ExamFeeExamDto
    {
        public Guid ExamId { get; set; }
        public string ExamName { get; set; } = "";
        public string Kind { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public bool HasSetup { get; set; }
        public DateTime? OpensAt { get; set; }
        public DateTime? ClosesAt { get; set; }
        public decimal? PerSubjectFee { get; set; }
        public int Applications { get; set; }
        public int Paid { get; set; }
    }

    public class ExamFeeCategoryRateDto
    {
        public string Category { get; set; } = "";
        public bool IsExempt { get; set; }
        public decimal? PerSubjectFee { get; set; }
        public decimal? FixedFee { get; set; }
    }

    public class ExamFeeInput
    {
        public DateTimeOffset? OpensAt { get; set; }
        public DateTimeOffset? ClosesAt { get; set; }
        public decimal PerSubjectFee { get; set; }
        public decimal? MaxSubjectFee { get; set; }
        public decimal FixedFee { get; set; }
        public string? FixedFeeLabel { get; set; }
        public DateTimeOffset? LateFeeFrom { get; set; }
        public decimal LateFee { get; set; }
        public string? Notes { get; set; }
        public List<ExamFeeCategoryRateDto> CategoryRates { get; set; } = new();
    }

    public class ExamFeeView
    {
        public Guid ExamId { get; set; }
        public string ExamName { get; set; } = "";
        public string Kind { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public bool Exists { get; set; }
        public DateTime? OpensAt { get; set; }
        public DateTime? ClosesAt { get; set; }
        public decimal PerSubjectFee { get; set; }
        public decimal? MaxSubjectFee { get; set; }
        public decimal FixedFee { get; set; }
        public string? FixedFeeLabel { get; set; }
        public DateTime? LateFeeFrom { get; set; }
        public decimal LateFee { get; set; }
        public string? Notes { get; set; }
        public List<ExamFeeCategoryRateDto> CategoryRates { get; set; } = new();
        /// <summary>Students have paid at these amounts: only the dates and notes can change.</summary>
        public bool AmountsLocked { get; set; }
        public int Applications { get; set; }
        public int Paid { get; set; }
        /// <summary>The categories in Student Master, for the category picker.</summary>
        public List<string> KnownCategories { get; set; } = new();
    }

    public class ExamFeeExample
    {
        public int Subjects { get; set; }
        public decimal OnTime { get; set; }
        /// <summary>With the late fee; null when there is none.</summary>
        public decimal? Late { get; set; }
    }

    public class ExamFeePreviewRow
    {
        /// <summary>Null = every student without a category row of their own.</summary>
        public string? Category { get; set; }
        public bool IsExempt { get; set; }
        public List<ExamFeeExample> Examples { get; set; } = new();
    }

    public interface IExamFeeService
    {
        /// <summary>The ATKT, Re-Exam and Revaluation exams of a class, with their fee setup if any.</summary>
        Task<List<ExamFeeExamDto>> GetExamsAsync(Guid courseId, Guid ayid);
        Task<ExamFeeView> GetAsync(Guid examId);
        Task<ExamFeeView> SaveAsync(Guid examId, ExamFeeInput input);
        Task DeleteAsync(Guid examId);
        /// <summary>What students would pay with these (unsaved) fees: per category, for 1–3 subjects.</summary>
        Task<List<ExamFeePreviewRow>> PreviewAsync(Guid examId, ExamFeeInput input);
    }

    /// <summary>Staff: the fee and application window of an ATKT, Re-Exam or Revaluation exam (step 1 of T-78).</summary>
    public sealed class ExamFeeService : IExamFeeService
    {
        public const decimal MaxAmount = 100000m;
        private const int MaxCategories = 30;

        private readonly ApplicationDbContext _context;

        public ExamFeeService(ApplicationDbContext context) => _context = context;

        /// <summary>The application kind of an exam, or null when students cannot apply for it.</summary>
        public static string? KindOf(ExamMaster exam) =>
            exam.RevaluationForExamId != null ? ExamApplicationKinds.Revaluation
            : ExamTypeKeys.IsAtkt(exam.ExamType) ? ExamApplicationKinds.Atkt
            : ExamTypeKeys.IsReExam(exam.ExamType) ? ExamApplicationKinds.ReExam
            : null;

        public static string DisplayName(ExamMaster exam) =>
            exam.RevaluationForExamId != null ? $"{exam.Name} (Revaluation)" : exam.Name;

        /// <summary>Times are stored in UTC; SQL Server gives them back without that mark, so the browser would read them as local.</summary>
        private static DateTime? Utc(DateTime? value) => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

        private Guid CollegeId => _context.CurrentCollegeId
            ?? throw new UnauthorizedAccessException("Sign in as staff of a college to set exam fees.");

        public async Task<List<ExamFeeExamDto>> GetExamsAsync(Guid courseId, Guid ayid)
        {
            _ = CollegeId;
            var exams = (await _context.Exams.AsNoTracking()
                    .Where(e => e.CourseId == courseId && e.AcademicYearAYID == ayid && e.IsActive == true)
                    .ToListAsync())
                .Where(e => KindOf(e) != null)
                .ToList();
            var ids = exams.Select(e => e.ExamId).ToList();
            var setups = await _context.ExamFeeSetups.AsNoTracking().Where(s => ids.Contains(s.ExamId)).ToListAsync();
            var counts = await CountsAsync(ids);

            return exams.Select(e =>
            {
                var s = setups.FirstOrDefault(x => x.ExamId == e.ExamId);
                var kind = KindOf(e)!;
                counts.TryGetValue(e.ExamId, out var c);
                return new ExamFeeExamDto
                {
                    ExamId = e.ExamId,
                    ExamName = DisplayName(e),
                    Kind = kind,
                    KindLabel = ExamApplicationKinds.Label(kind),
                    HasSetup = s != null,
                    OpensAt = Utc(s?.OpensAt),
                    ClosesAt = Utc(s?.ClosesAt),
                    PerSubjectFee = s?.PerSubjectFee,
                    Applications = c.Applications,
                    Paid = c.Paid,
                };
            }).OrderBy(e => e.KindLabel).ThenBy(e => e.ExamName).ToList();
        }

        public async Task<ExamFeeView> GetAsync(Guid examId)
        {
            _ = CollegeId;
            var (exam, kind) = await LoadExamAsync(examId);
            var setup = await LoadSetupAsync(examId);
            var counts = await CountsAsync(new List<Guid> { examId });
            counts.TryGetValue(examId, out var c);
            var categories = await _context.StudentMasters.AsNoTracking()
                .Where(s => s.Category != null && s.Category != "")
                .Select(s => s.Category!.Trim())
                .Distinct()
                .ToListAsync();

            var view = new ExamFeeView
            {
                ExamId = exam.ExamId,
                ExamName = DisplayName(exam),
                Kind = kind,
                KindLabel = ExamApplicationKinds.Label(kind),
                Exists = setup != null,
                Applications = c.Applications,
                Paid = c.Paid,
                AmountsLocked = c.Paid > 0,
                KnownCategories = categories.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            };
            if (setup == null) return view;

            view.OpensAt = Utc(setup.OpensAt);
            view.ClosesAt = Utc(setup.ClosesAt);
            view.PerSubjectFee = setup.PerSubjectFee;
            view.MaxSubjectFee = setup.MaxSubjectFee;
            view.FixedFee = setup.FixedFee;
            view.FixedFeeLabel = setup.FixedFeeLabel;
            view.LateFeeFrom = Utc(setup.LateFeeFrom);
            view.LateFee = setup.LateFee;
            view.Notes = setup.Notes;
            view.CategoryRates = setup.CategoryRates
                .OrderBy(r => r.Category)
                .Select(r => new ExamFeeCategoryRateDto { Category = r.Category, IsExempt = r.IsExempt, PerSubjectFee = r.PerSubjectFee, FixedFee = r.FixedFee })
                .ToList();
            return view;
        }

        public async Task<ExamFeeView> SaveAsync(Guid examId, ExamFeeInput input)
        {
            var collegeId = CollegeId;
            var (exam, kind) = await LoadExamAsync(examId);
            var clean = Validate(input, kind);
            var setup = await LoadSetupAsync(examId, tracking: true);
            var counts = await CountsAsync(new List<Guid> { examId });
            counts.TryGetValue(examId, out var c);

            if (setup != null && c.Paid > 0 && !SameAmounts(setup, clean))
                throw new InvalidOperationException(
                    $"{c.Paid} student(s) have already paid at these fees. You can change the dates and notes; the amounts are locked.");

            var isNew = setup == null;
            if (setup == null)
            {
                setup = new ExamFeeSetup { Id = Guid.NewGuid(), CollegeId = collegeId, ExamId = examId, Kind = kind };
                _context.ExamFeeSetups.Add(setup);
            }
            else
            {
                setup.UpdatedAt = DateTime.UtcNow;
            }
            setup.Kind = kind;
            setup.OpensAt = clean.OpensAt;
            setup.ClosesAt = clean.ClosesAt;
            setup.PerSubjectFee = clean.PerSubjectFee;
            setup.MaxSubjectFee = clean.MaxSubjectFee;
            setup.FixedFee = clean.FixedFee;
            setup.FixedFeeLabel = clean.FixedFeeLabel;
            setup.LateFeeFrom = clean.LateFeeFrom;
            setup.LateFee = clean.LateFee;
            setup.Notes = clean.Notes;

            // Category rows are replaced as a whole (they are small and have no history of their own).
            _context.ExamFeeCategoryRates.RemoveRange(setup.CategoryRates);
            setup.CategoryRates.Clear();
            foreach (var r in clean.CategoryRates)
            {
                setup.CategoryRates.Add(new ExamFeeCategoryRate
                {
                    Id = Guid.NewGuid(), CollegeId = collegeId, FeeSetupId = setup.Id, Category = r.Category,
                    IsExempt = r.IsExempt, PerSubjectFee = r.PerSubjectFee, FixedFee = r.FixedFee,
                });
            }

            Audit(collegeId, setup.Id, isNew ? "ExamFeeAdded" : "ExamFeeChanged",
                $"{DisplayName(exam)}: ₹{FeeCalculator.Money(clean.PerSubjectFee)} per subject"
                + (clean.MaxSubjectFee is { } m ? $" (at most ₹{FeeCalculator.Money(m)})" : "")
                + (clean.FixedFee > 0 ? $" + ₹{FeeCalculator.Money(clean.FixedFee)} {clean.FixedFeeLabel ?? "fixed"}" : "")
                + (clean.LateFeeFrom != null ? $", late fee ₹{FeeCalculator.Money(clean.LateFee)} from {clean.LateFeeFrom:yyyy-MM-dd HH:mm} UTC" : "")
                + $", closes {clean.ClosesAt:yyyy-MM-dd HH:mm} UTC, {clean.CategoryRates.Count} category row(s).");
            await _context.SaveChangesAsync();
            return await GetAsync(examId);
        }

        public async Task DeleteAsync(Guid examId)
        {
            var collegeId = CollegeId;
            var (exam, _) = await LoadExamAsync(examId);
            var setup = await LoadSetupAsync(examId, tracking: true);
            if (setup == null) return;
            var applied = await _context.ExamApplications.CountAsync(a => a.TargetExamId == examId
                && a.Status != ExamApplicationStatuses.Cancelled);
            if (applied > 0)
                throw new InvalidOperationException($"{applied} student(s) have applied for this exam, so its fees can't be removed. Move the closing date instead.");
            setup.IsDeleted = true;
            setup.DeletedAt = DateTime.UtcNow;
            Audit(collegeId, setup.Id, "ExamFeeRemoved", $"{DisplayName(exam)}: fees removed.");
            await _context.SaveChangesAsync();
        }

        public async Task<List<ExamFeePreviewRow>> PreviewAsync(Guid examId, ExamFeeInput input)
        {
            _ = CollegeId;
            var (_, kind) = await LoadExamAsync(examId);
            var clean = Validate(input, kind);
            var setup = new ExamFeeSetup
            {
                Kind = kind, ClosesAt = clean.ClosesAt, PerSubjectFee = clean.PerSubjectFee, MaxSubjectFee = clean.MaxSubjectFee,
                FixedFee = clean.FixedFee, FixedFeeLabel = clean.FixedFeeLabel, LateFeeFrom = clean.LateFeeFrom, LateFee = clean.LateFee,
                CategoryRates = clean.CategoryRates.Select(r => new ExamFeeCategoryRate
                {
                    Category = r.Category, IsExempt = r.IsExempt, PerSubjectFee = r.PerSubjectFee, FixedFee = r.FixedFee,
                }).ToList(),
            };
            var categories = new List<string?> { null };
            categories.AddRange(setup.CategoryRates.Select(r => (string?)r.Category));

            var beforeLate = (clean.LateFeeFrom ?? clean.ClosesAt).AddMinutes(-1);
            var late = clean.LateFeeFrom;
            return categories.Select(cat =>
            {
                var rate = cat == null ? null : FeeCalculator.FindRate(setup, cat);
                // The default row is for students whose category has no row: use a category no row can match.
                var asCategory = cat ?? "\u0000";
                return new ExamFeePreviewRow
                {
                    Category = cat,
                    IsExempt = rate?.IsExempt == true,
                    Examples = Enumerable.Range(1, 3).Select(n => new ExamFeeExample
                    {
                        Subjects = n,
                        OnTime = FeeCalculator.Compute(setup, asCategory, n, beforeLate).Total,
                        Late = late != null && clean.LateFee > 0 && rate?.IsExempt != true
                            ? FeeCalculator.Compute(setup, asCategory, n, late.Value).Total
                            : null,
                    }).ToList(),
                };
            }).ToList();
        }

        // ------------------------------------------------------------------

        private sealed record CleanInput(
            DateTime? OpensAt, DateTime ClosesAt, decimal PerSubjectFee, decimal? MaxSubjectFee, decimal FixedFee,
            string? FixedFeeLabel, DateTime? LateFeeFrom, decimal LateFee, string? Notes, List<ExamFeeCategoryRateDto> CategoryRates);

        private static CleanInput Validate(ExamFeeInput input, string kind)
        {
            if (input.ClosesAt == null)
                throw new InvalidOperationException("Enter the closing date: applications and payments stop then.");
            var closes = input.ClosesAt.Value.UtcDateTime;
            var opens = input.OpensAt?.UtcDateTime;
            if (opens != null && opens >= closes)
                throw new InvalidOperationException("The opening date must be before the closing date.");

            CheckAmount(input.PerSubjectFee, kind == ExamApplicationKinds.Revaluation ? "The fee per paper" : "The fee per subject");
            CheckAmount(input.FixedFee, "The fixed fee");
            CheckAmount(input.LateFee, "The late fee");
            if (input.MaxSubjectFee is { } max)
            {
                CheckAmount(max, "The maximum for subjects");
                if (max < input.PerSubjectFee)
                    throw new InvalidOperationException("The maximum for subjects can't be less than the fee for one subject.");
            }

            var label = (input.FixedFeeLabel ?? "").Trim();
            if (label.Length > 100) throw new InvalidOperationException("The fixed fee's name can be at most 100 characters.");
            if (input.FixedFee > 0 && label == "")
                throw new InvalidOperationException("Name the fixed fee, e.g. \"Statement of marks\".");

            DateTime? lateFrom = input.LateFeeFrom?.UtcDateTime;
            if (input.LateFee > 0 && lateFrom == null)
                throw new InvalidOperationException("Enter the date the late fee starts.");
            if (lateFrom != null)
            {
                if (input.LateFee <= 0) throw new InvalidOperationException("Enter the late fee amount, or clear its start date.");
                if (lateFrom >= closes) throw new InvalidOperationException("The late fee must start before the closing date.");
                if (opens != null && lateFrom <= opens) throw new InvalidOperationException("The late fee must start after the opening date.");
            }

            var notes = (input.Notes ?? "").Trim();
            if (notes.Length > 1000) throw new InvalidOperationException("Notes can be at most 1000 characters.");

            var rates = new List<ExamFeeCategoryRateDto>();
            foreach (var r in input.CategoryRates ?? new())
            {
                var cat = (r.Category ?? "").Trim();
                if (cat == "") throw new InvalidOperationException("Every category row needs a category.");
                if (cat.Length > 50) throw new InvalidOperationException("A category can be at most 50 characters.");
                if (rates.Any(x => FeeCalculator.NormalizeCategory(x.Category) == FeeCalculator.NormalizeCategory(cat)))
                    throw new InvalidOperationException($"The category \"{cat}\" is listed twice.");
                if (!r.IsExempt)
                {
                    if (r.PerSubjectFee == null && r.FixedFee == null)
                        throw new InvalidOperationException($"Category \"{cat}\": enter its fee, or tick \"No fee\".");
                    if (r.PerSubjectFee is { } p) CheckAmount(p, $"Category \"{cat}\": the fee per subject");
                    if (r.FixedFee is { } f) CheckAmount(f, $"Category \"{cat}\": the fixed fee");
                }
                rates.Add(new ExamFeeCategoryRateDto
                {
                    Category = cat, IsExempt = r.IsExempt,
                    PerSubjectFee = r.IsExempt ? null : r.PerSubjectFee,
                    FixedFee = r.IsExempt ? null : r.FixedFee,
                });
            }
            if (rates.Count > MaxCategories) throw new InvalidOperationException($"At most {MaxCategories} category rows.");

            return new CleanInput(opens, closes, input.PerSubjectFee, input.MaxSubjectFee, input.FixedFee,
                label == "" ? null : label, lateFrom, lateFrom == null ? 0 : input.LateFee, notes == "" ? null : notes, rates);
        }

        private static void CheckAmount(decimal amount, string what)
        {
            if (amount < 0) throw new InvalidOperationException($"{what} can't be negative.");
            if (amount > MaxAmount) throw new InvalidOperationException($"{what} can be at most ₹{FeeCalculator.Money(MaxAmount)}.");
            if (decimal.Round(amount, 2) != amount) throw new InvalidOperationException($"{what} can have at most 2 decimals.");
        }

        private static bool SameAmounts(ExamFeeSetup s, CleanInput c)
        {
            if (s.PerSubjectFee != c.PerSubjectFee || s.MaxSubjectFee != c.MaxSubjectFee || s.FixedFee != c.FixedFee
                || s.LateFee != c.LateFee || (s.LateFeeFrom == null) != (c.LateFeeFrom == null))
                return false;
            if (s.CategoryRates.Count != c.CategoryRates.Count) return false;
            return c.CategoryRates.All(r =>
            {
                var old = s.CategoryRates.FirstOrDefault(o => FeeCalculator.NormalizeCategory(o.Category) == FeeCalculator.NormalizeCategory(r.Category));
                return old != null && old.IsExempt == r.IsExempt && old.PerSubjectFee == r.PerSubjectFee && old.FixedFee == r.FixedFee;
            });
        }

        private async Task<(ExamMaster Exam, string Kind)> LoadExamAsync(Guid examId)
        {
            var exam = await _context.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.ExamId == examId)
                ?? throw new KeyNotFoundException("Exam not found.");
            var kind = KindOf(exam)
                ?? throw new InvalidOperationException("Fees can only be set for an A.T.K.T, Re-Exam or Revaluation exam.");
            return (exam, kind);
        }

        private Task<ExamFeeSetup?> LoadSetupAsync(Guid examId, bool tracking = false)
        {
            var q = _context.ExamFeeSetups.Include(s => s.CategoryRates).Where(s => s.ExamId == examId);
            return (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync();
        }

        private async Task<Dictionary<Guid, (int Applications, int Paid)>> CountsAsync(List<Guid> examIds)
        {
            var rows = await _context.ExamApplications.AsNoTracking()
                .Where(a => examIds.Contains(a.TargetExamId) && a.Status != ExamApplicationStatuses.Cancelled)
                .Select(a => new { a.TargetExamId, a.Status })
                .ToListAsync();
            return rows.GroupBy(r => r.TargetExamId).ToDictionary(
                g => g.Key,
                g => (g.Count(), g.Count(r => ExamApplicationStatuses.IsPaid(r.Status) || r.Status == ExamApplicationStatuses.Refunded)));
        }

        private void Audit(Guid collegeId, Guid setupId, string evt, string details) =>
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CollegeId = collegeId,
                Action = "UPDATE",
                TableName = nameof(ExamFeeSetup),
                RecordId = setupId.ToString(),
                PerformedBy = _context.CurrentUserId,
                UserType = "Staff",
                Event = evt,
                Details = details.Length > 1000 ? details[..1000] : details,
            });
    }
}
