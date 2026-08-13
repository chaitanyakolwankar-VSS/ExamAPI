using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Result.Engine;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace ExamAPI.Services.AtktRevalExam
{
    /// <summary>
    /// Assignment into a follow-up attempt. The legacy screen encoded "is this student
    /// appearing for this head?" as a trailing '+' inside the marks string; here it is
    /// <see cref="StudentMarks.IsCarryForward"/> -- false means appearing, true means the
    /// mark is carried over from the source attempt. Every other legacy literal (which exam
    /// types qualify, whether a cleared subject can be re-registered, which heads are
    /// revaluable) comes from <see cref="ExamAssignmentPolicy"/> rows.
    /// </summary>
    public class AtktRevalExamService : IAtktRevalExamService
    {
        private readonly ApplicationDbContext _context;
        private readonly IGenericRepository _genericRepository;
        private readonly EngineRegistry _registry;

        public AtktRevalExamService(
            ApplicationDbContext context,
            IGenericRepository genericRepository,
            EngineRegistry registry)
        {
            _context = context;
            _genericRepository = genericRepository;
            _registry = registry;
        }

        private const string StatusNotAttempted = "NotAttempted";

        // =====================================================================
        // Policy
        // =====================================================================

        public async Task<List<AtktPolicyDto>> GetPoliciesAsync(string? mode)
        {
            var query = _context.ExamAssignmentPolicies
                .Include(p => p.RuleSet)
                .Where(p => p.IsEnabled);

            if (!string.IsNullOrWhiteSpace(mode))
            {
                query = query.Where(p => p.Mode == mode);
            }

            var policies = await query.AsNoTracking().ToListAsync();

            if (policies.Count == 0)
            {
                // This college has no policy row yet -- surface the built-in defaults so the
                // screen is usable before SeedExamAssignmentPolicies.sql has been run.
                return string.IsNullOrWhiteSpace(mode)
                    ? new List<AtktPolicyDto>
                    {
                        ToDto(DefaultPolicy(AssignmentModes.Atkt)),
                        ToDto(DefaultPolicy(AssignmentModes.Revaluation))
                    }
                    : new List<AtktPolicyDto> { ToDto(DefaultPolicy(mode)) };
            }

            return policies.Select(ToDto).ToList();
        }

        /// <summary>
        /// Picks this college's policy for a mode -- the global query filter has already
        /// restricted the table to the caller's tenant. A pattern-specific row beats a
        /// pattern-agnostic one; with nothing configured the built-in default keeps the
        /// screen working.
        /// </summary>
        private async Task<ExamAssignmentPolicy> ResolvePolicyAsync(string mode, Guid? policyId, string? pattern)
        {
            var normalized = AssignmentModes.IsRevaluation(mode) ? AssignmentModes.Revaluation : AssignmentModes.Atkt;

            if (policyId.HasValue && policyId.Value != Guid.Empty)
            {
                var explicitPolicy = await _context.ExamAssignmentPolicies
                    .Include(p => p.RuleSet)
                    .FirstOrDefaultAsync(p => p.PolicyId == policyId.Value);
                if (explicitPolicy != null) return explicitPolicy;
            }

            var patternId = string.IsNullOrWhiteSpace(pattern)
                ? (Guid?)null
                : await _context.PatternMasters
                    .Where(p => p.PatternName == pattern)
                    .Select(p => (Guid?)p.PatternId)
                    .FirstOrDefaultAsync();

            var candidates = await _context.ExamAssignmentPolicies
                .Include(p => p.RuleSet)
                .Where(p => p.IsEnabled && p.Mode == normalized)
                .ToListAsync();

            var resolved = candidates
                .Where(p => p.PatternId == null || p.PatternId == patternId)
                .OrderByDescending(p => p.PatternId != null)
                .FirstOrDefault();

            return resolved ?? DefaultPolicy(normalized);
        }

        /// <summary>
        /// The behaviour the legacy form hard-coded, used only when no policy row exists.
        /// Editing a seeded row is the supported way to change any of this.
        /// </summary>
        private static ExamAssignmentPolicy DefaultPolicy(string mode) =>
            AssignmentModes.IsRevaluation(mode)
                ? new ExamAssignmentPolicy
                {
                    Name = "Default Revaluation policy",
                    Mode = AssignmentModes.Revaluation,
                    SourceExamTypes = "Regular,ATKT,KT,Re-Exam",
                    TargetExamTypes = string.Empty,
                    RequireFailedSubject = false,
                    OfferPassedSubjects = true,
                    BlockAbsentStudents = true,
                    AutoSelectFailedSubjects = false,
                    EligibleHeadTypes = "ESE",
                    CarryForwardSeatNo = true,
                    CarryForwardMarks = true,
                    BlockDeleteAfterMarksEntry = true,
                    IsEnabled = true,
                    SubjectsPerRow = 7
                }
                : new ExamAssignmentPolicy
                {
                    Name = "Default ATKT policy",
                    Mode = AssignmentModes.Atkt,
                    SourceExamTypes = "Regular,ATKT,KT,Re-Exam",
                    TargetExamTypes = "ATKT,KT,Re-Exam",
                    RequireFailedSubject = true,
                    OfferPassedSubjects = false,
                    BlockAbsentStudents = false,
                    AutoSelectFailedSubjects = true,
                    EligibleHeadTypes = string.Empty,
                    CarryForwardSeatNo = true,
                    CarryForwardMarks = true,
                    BlockDeleteAfterMarksEntry = true,
                    IsEnabled = true,
                    SubjectsPerRow = 7
                };

        private static AtktPolicyDto ToDto(ExamAssignmentPolicy p) => new()
        {
            PolicyId = p.PolicyId,
            Name = p.Name,
            Mode = p.Mode,
            RequireFailedSubject = p.RequireFailedSubject,
            OfferPassedSubjects = p.OfferPassedSubjects,
            BlockAbsentStudents = p.BlockAbsentStudents,
            AutoSelectFailedSubjects = p.AutoSelectFailedSubjects,
            CarryForwardSeatNo = p.CarryForwardSeatNo,
            CarryForwardMarks = p.CarryForwardMarks,
            BlockDeleteAfterMarksEntry = p.BlockDeleteAfterMarksEntry,
            MaxSubjectsPerStudent = p.MaxSubjectsPerStudent,
            SubjectsPerRow = p.SubjectsPerRow <= 0 ? 7 : p.SubjectsPerRow,
            EligibleHeadTypes = SplitCsv(p.EligibleHeadTypes),
            SourceExamTypes = SplitCsv(p.SourceExamTypes),
            TargetExamTypes = SplitCsv(p.TargetExamTypes),
            HasEligibilityRules = p.RuleSetId.HasValue,
            RuleSetName = p.RuleSet?.Name
        };

        private static List<string> SplitCsv(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static bool MatchesExamType(string? examType, List<string> allowed) =>
            allowed.Count == 0 || allowed.Any(a => string.Equals(a, examType, StringComparison.OrdinalIgnoreCase));

        // =====================================================================
        // Exam pickers
        // =====================================================================

        public async Task<List<AtktExamOptionDto>> GetSourceExamsAsync(
            Guid courseId, Guid ayid, string semester, string pattern, string mode)
        {
            var policy = await ResolvePolicyAsync(mode, null, pattern);
            var allowed = SplitCsv(policy.SourceExamTypes);

            // An exam only qualifies as a source once it actually holds marks for this
            // semester -- the legacy screen made the same restriction with a sub-select.
            var examIdsWithMarks = await _context.MarksMasters
                .Where(mm => mm.AcademicYearAYID == ayid && mm.SemesterId == semester && mm.ExamId != null)
                .Select(mm => mm.ExamId!.Value)
                .Distinct()
                .ToListAsync();

            var exams = await _context.Exams
                .Where(e => e.CourseId == courseId
                            && e.AcademicYearAYID == ayid
                            && e.RevaluationForExamId == null
                            && examIdsWithMarks.Contains(e.ExamId))
                .AsNoTracking()
                .ToListAsync();

            return exams
                .Where(e => MatchesExamType(e.ExamType, allowed))
                .Select(ToExamOption)
                .OrderBy(e => e.ExamName)
                .ToList();
        }

        public async Task<List<AtktExamOptionDto>> GetTargetExamsAsync(
            Guid courseId, Guid ayid, string semester, string mode, Guid? sourceExamId)
        {
            var isReval = AssignmentModes.IsRevaluation(mode);
            var policy = await ResolvePolicyAsync(mode, null, null);
            var allowed = SplitCsv(policy.TargetExamTypes);

            var query = _context.Exams
                .Where(e => e.CourseId == courseId && e.AcademicYearAYID == ayid && e.IsActive == true);

            if (isReval)
            {
                // The revaluation target is the mirror exam of the chosen source, not a free choice.
                query = sourceExamId.HasValue && sourceExamId.Value != Guid.Empty
                    ? query.Where(e => e.RevaluationForExamId == sourceExamId.Value)
                    : query.Where(e => e.RevaluationForExamId != null);
            }
            else
            {
                query = query.Where(e => e.RevaluationForExamId == null);
            }

            var exams = await query.AsNoTracking().ToListAsync();

            return exams
                .Where(e => isReval || MatchesExamType(e.ExamType, allowed))
                .Select(ToExamOption)
                .OrderBy(e => e.ExamName)
                .ToList();
        }

        private static AtktExamOptionDto ToExamOption(ExamMaster e) => new()
        {
            ExamId = e.ExamId,
            ExamName = e.RevaluationForExamId != null ? $"{e.Name} (Revaluation)" : e.Name,
            ExamType = e.ExamType,
            IsRevaluation = e.RevaluationForExamId != null,
            RevaluationForExamId = e.RevaluationForExamId,
            IsLocked = e.IsLocked
        };

        // =====================================================================
        // Matrix
        // =====================================================================

        public async Task<AtktMatrixResponseDto> GetMatrixAsync(AtktMatrixRequest request)
        {
            var build = await BuildAsync(request, includeAllCandidates: false);
            return build.Response;
        }

        /// <summary>Everything one screenful needs, kept together so save can re-validate against it.</summary>
        private sealed class MatrixBuild
        {
            public AtktMatrixResponseDto Response { get; } = new();
            public ExamAssignmentPolicy Policy { get; set; } = null!;
            public ExamMaster? TargetExam { get; set; }
            public Dictionary<Guid, StudentContext> Contexts { get; } = new();
            public Dictionary<Guid, SubjectCreditMaster> CreditBySubject { get; } = new();
        }

        private sealed class StudentContext
        {
            public StudentMaster Student { get; set; } = null!;
            public MarksMaster? Source { get; set; }
            public MarksMaster? Target { get; set; }
            public AtktStudentRowDto Row { get; set; } = null!;
        }

        private async Task<MatrixBuild> BuildAsync(AtktMatrixRequest request, bool includeAllCandidates)
        {
            var build = new MatrixBuild();
            var response = build.Response;

            var isReval = AssignmentModes.IsRevaluation(request.Mode);
            var policy = await ResolvePolicyAsync(request.Mode, request.PolicyId, request.Pattern);
            build.Policy = policy;
            response.Policy = ToDto(policy);

            var targetExam = await _context.Exams.FirstOrDefaultAsync(e => e.ExamId == request.TargetExamId);
            build.TargetExam = targetExam;
            if (targetExam == null)
            {
                response.Success = false;
                response.Message = "Select the exam to assign students to.";
                return build;
            }

            if (isReval && (request.SourceExamId == null || request.SourceExamId == Guid.Empty))
            {
                response.Success = false;
                response.Message = "Revaluation needs the source exam whose marks are being revalued.";
                return build;
            }

            // ---- roster ---------------------------------------------------------
            var roster = await _context.StudentEligibilities
                .Where(se => se.CourseId == request.CourseId
                             && se.AYID == request.Ayid
                             && se.SemesterId == request.Semester
                             && se.Pattern == request.Pattern
                             && se.StdMstId != null)
                .Join(_context.StudentMasters, se => se.StdMstId, sm => sm.StdMstId, (se, sm) => sm)
                .Distinct()
                .ToListAsync();

            if (roster.Count == 0)
            {
                response.Success = true;
                response.Message = "No students are enrolled for this branch, semester and pattern in the selected academic year.";
                return build;
            }

            var rosterIds = roster.Select(s => s.StdMstId).ToHashSet();

            // ---- attempts (source + target) --------------------------------------
            var attempts = await _context.MarksMasters
                .Include(mm => mm.Exam)
                .Include(mm => mm.StudentMarks!)
                    .ThenInclude(sm => sm.CreditMaster!)
                        .ThenInclude(cm => cm.Credits)
                .Where(mm => mm.StdMstId != null
                             && rosterIds.Contains(mm.StdMstId.Value)
                             && mm.AcademicYearAYID == request.Ayid
                             && mm.SemesterId == request.Semester
                             && mm.Pattern == request.Pattern)
                .ToListAsync();

            var attemptsByStudent = attempts
                .Where(a => a.StdMstId.HasValue)
                .GroupBy(a => a.StdMstId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var attemptIds = attempts.Select(a => a.MarksId).ToList();
            var subjectResults = await _context.StudentSubjectResults
                .Where(r => attemptIds.Contains(r.MarksId))
                .AsNoTracking()
                .ToListAsync();
            var resultsByMarks = subjectResults
                .GroupBy(r => r.MarksId)
                .ToDictionary(g => g.Key, g => g.GroupBy(r => r.SubjectId).ToDictionary(x => x.Key, x => x.First()));

            // ---- columns ----------------------------------------------------------
            var subjects = await _context.SubjectMasters
                .Where(s => s.CourseId == request.CourseId
                            && s.SemId == request.Semester
                            && s.Pattern == request.Pattern)
                .OrderBy(s => s.SubjectCode)
                .AsNoTracking()
                .ToListAsync();

            var subjectIds = subjects.Select(s => s.SubjectId).ToList();
            var creditMasters = await _context.SubjectCreditMasters
                .Include(cm => cm.Credits)
                .Where(cm => cm.SubjectId != null && subjectIds.Contains(cm.SubjectId.Value))
                .AsNoTracking()
                .ToListAsync();

            // SubjectCreditMaster.AYID is a 20-character string column, so a full GUID does not
            // always fit and older rows hold other formats. Prefer the row for this year and
            // fall back to whatever is configured rather than rendering an empty grid.
            var ayidText = request.Ayid.ToString();
            foreach (var group in creditMasters.Where(cm => cm.SubjectId.HasValue).GroupBy(cm => cm.SubjectId!.Value))
            {
                build.CreditBySubject[group.Key] = group.FirstOrDefault(cm => cm.AYID == ayidText) ?? group.First();
            }

            var order = 0;
            foreach (var subject in subjects)
            {
                build.CreditBySubject.TryGetValue(subject.SubjectId, out var credit);
                var heads = (credit?.Credits ?? new List<SubjectCredits>())
                    .OrderBy(h => h.Head)
                    .Select(h => new AtktHeadDto
                    {
                        Head = h.Head ?? string.Empty,
                        HeadType = string.IsNullOrWhiteSpace(h.HeadType) ? h.Head ?? string.Empty : h.HeadType,
                        OutOf = ParseInt(h.HeadOutOf),
                        Pass = ParseInt(h.HeadPass)
                    })
                    .ToList();

                var outOfTotal = heads.Sum(h => h.OutOf);
                var isCombined = SubjectPassEvaluator.IsCombined(credit);
                var requiredToPass = isCombined && credit?.PassPercentage is > 0
                    ? (int)Math.Ceiling(outOfTotal * credit.PassPercentage!.Value / 100.0)
                    : heads.Sum(h => h.Pass);

                response.Columns.Add(new AtktSubjectColumnDto
                {
                    SubjectId = subject.SubjectId,
                    CreditsId = credit?.CreditsId ?? Guid.Empty,
                    SubjectCode = subject.SubjectCode,
                    SubjectName = subject.Name,
                    PassingStrategy = credit?.PassingStrategy ?? PassingStrategies.HeadWise,
                    OutOfTotal = outOfTotal,
                    RequiredToPass = requiredToPass,
                    Order = order++,
                    Heads = heads
                });
            }

            if (response.Columns.Count == 0)
            {
                response.Success = true;
                response.Message = "No subjects are configured for this branch, semester and pattern.";
                return build;
            }

            // ---- eligibility rule gate --------------------------------------------
            List<Rule>? eligibilityRules = null;
            if (policy.RuleSetId.HasValue)
            {
                eligibilityRules = await _context.Rules
                    .Include(r => r.Conditions)
                    .Where(r => r.RuleSetId == policy.RuleSetId.Value && r.IsEnabled)
                    .ToListAsync();
            }

            var eligibleHeadTypes = SplitCsv(policy.EligibleHeadTypes);

            // ---- rows ---------------------------------------------------------------
            foreach (var student in roster.OrderBy(s => s.StudentId))
            {
                attemptsByStudent.TryGetValue(student.StdMstId, out var studentAttempts);
                studentAttempts ??= new List<MarksMaster>();

                var target = studentAttempts.FirstOrDefault(a => a.ExamId == request.TargetExamId);
                var source = isReval
                    ? studentAttempts.FirstOrDefault(a => a.ExamId == request.SourceExamId)
                    : studentAttempts
                        .Where(a => a.ExamId != request.TargetExamId)
                        .OrderByDescending(a => a.CreatedAt)
                        .FirstOrDefault();

                var isAssigned = target != null;

                if (!includeAllCandidates)
                {
                    if (request.EditMode && !isAssigned) continue;
                    if (!request.EditMode && isAssigned) continue;
                }

                if (source == null && target == null) continue;

                if (eligibilityRules is { Count: > 0 } && source != null)
                {
                    if (!await RuleConditionEvaluator.IsEligibleAsync(_registry, eligibilityRules, student, source))
                    {
                        continue;
                    }
                }

                var sourceHeads = source?.StudentMarks?.ToList() ?? new List<StudentMarks>();
                var targetHeads = target?.StudentMarks?.ToList() ?? new List<StudentMarks>();
                resultsByMarks.TryGetValue(source?.MarksId ?? Guid.Empty, out var studentResults);

                var row = new AtktStudentRowDto
                {
                    StdMstId = student.StdMstId,
                    StudentId = student.StudentId,
                    StudentName = BuildName(student),
                    SeatNo = target?.SeatNo ?? source?.SeatNo,
                    SourceMarksId = source?.MarksId,
                    SourceExamId = source?.ExamId,
                    SourceExamName = source?.Exam?.Name,
                    TargetMarksId = target?.MarksId,
                    IsAssigned = isAssigned
                };

                foreach (var column in response.Columns)
                {
                    var group = sourceHeads.Where(h => h.SubjectId == column.SubjectId).ToList();
                    var cell = new AtktCellDto
                    {
                        SubjectId = column.SubjectId,
                        CreditsId = column.CreditsId,
                        OutOfTotal = column.OutOfTotal,
                        RequiredToPass = column.RequiredToPass
                    };

                    if (group.Count == 0)
                    {
                        cell.Status = StatusNotAttempted;
                    }
                    else if (studentResults != null && studentResults.TryGetValue(column.SubjectId, out var computed))
                    {
                        // Result processing has already ruled on this subject -- that verdict wins.
                        cell.Status = computed.SubjectStatus;
                        cell.ObtainedTotal = computed.ObtainedTotal;
                        cell.OutOfTotal = computed.OutOfTotal;
                        cell.IsAbsent = computed.SubjectStatus == SubjectStatuses.Absent;
                    }
                    else
                    {
                        var verdict = SubjectPassEvaluator.Evaluate(group);
                        cell.ObtainedTotal = verdict.ObtainedTotal;
                        cell.OutOfTotal = verdict.OutOfTotal;
                        cell.RequiredToPass = verdict.RequiredToPass;
                        cell.IsAbsent = verdict.IsAllAbsent;
                        cell.Status = verdict.IsAllAbsent
                            ? SubjectStatuses.Absent
                            : verdict.IsPassed ? SubjectStatuses.Passed : SubjectStatuses.Failed;
                    }

                    ApplySelectability(cell, column, policy, eligibleHeadTypes, isReval);

                    cell.Selected = isAssigned
                        ? targetHeads.Any(h => h.SubjectId == column.SubjectId && !h.IsCarryForward)
                        : cell.Selectable && policy.AutoSelectFailedSubjects && IsBacklog(cell.Status);

                    row.Cells.Add(cell);
                }

                row.BacklogCount = row.Cells.Count(c =>
                    c.Status == SubjectStatuses.Failed || c.Status == SubjectStatuses.Absent);

                if (isAssigned)
                {
                    var marksEntered = targetHeads.Any(h => !h.IsCarryForward && (h.Marks.HasValue || h.IsAbsent))
                                       || !string.IsNullOrWhiteSpace(target!.OverallRemark);
                    row.CanDelete = !(policy.BlockDeleteAfterMarksEntry && marksEntered);
                    row.DeleteBlockedReason = row.CanDelete
                        ? null
                        : "Marks have already been entered for this student in the selected exam.";
                }

                // In ATKT mode a student with nothing outstanding has no reason to be listed.
                if (!includeAllCandidates && !request.EditMode && policy.RequireFailedSubject
                    && !row.Cells.Any(c => c.Selectable))
                {
                    continue;
                }

                build.Contexts[student.StdMstId] = new StudentContext
                {
                    Student = student,
                    Source = source,
                    Target = target,
                    Row = row
                };
                response.Students.Add(row);
            }

            response.Success = true;
            response.Message = response.Students.Count == 0
                ? request.EditMode
                    ? "No students are assigned to this exam yet."
                    : "No eligible students found for this exam."
                : $"{response.Students.Count} student(s) loaded.";

            return build;
        }

        /// <summary>Decides whether the operator may tick a cell, entirely from the policy row.</summary>
        private static void ApplySelectability(
            AtktCellDto cell,
            AtktSubjectColumnDto column,
            ExamAssignmentPolicy policy,
            List<string> eligibleHeadTypes,
            bool isReval)
        {
            if (column.Heads.Count == 0)
            {
                cell.Selectable = false;
                cell.Reason = "No heads are configured for this subject.";
                return;
            }

            if (eligibleHeadTypes.Count > 0 &&
                !column.Heads.Any(h => eligibleHeadTypes.Contains(h.HeadType, StringComparer.OrdinalIgnoreCase)))
            {
                cell.Selectable = false;
                cell.Reason = $"Only {string.Join(", ", eligibleHeadTypes)} heads can be applied for.";
                return;
            }

            if (!policy.RequireFailedSubject && cell.Status != StatusNotAttempted)
            {
                cell.Selectable = true;
                return;
            }

            switch (cell.Status)
            {
                case SubjectStatuses.Passed when !policy.OfferPassedSubjects:
                    cell.Selectable = false;
                    cell.Reason = "Already cleared.";
                    break;
                case SubjectStatuses.Absent when policy.BlockAbsentStudents:
                    cell.Selectable = false;
                    cell.Reason = "Marked absent -- nothing to revalue.";
                    break;
                case StatusNotAttempted when isReval:
                    cell.Selectable = false;
                    cell.Reason = "No marks recorded in the source exam.";
                    break;
                default:
                    cell.Selectable = true;
                    break;
            }
        }

        private static bool IsBacklog(string status) =>
            status == SubjectStatuses.Failed || status == SubjectStatuses.Absent || status == StatusNotAttempted;

        private static string BuildName(StudentMaster s) =>
            string.Join(" ", new[] { s.FirstName, s.MiddleName, s.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        private static int ParseInt(string? value) => int.TryParse(value, out var parsed) ? parsed : 0;

        // =====================================================================
        // Save
        // =====================================================================

        public async Task<ApiResponseDto<AtktSaveResultDto>> SaveAsync(AtktSaveRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var build = await BuildAsync(request.Filter, includeAllCandidates: true);
                if (build.TargetExam == null) return Fail(build.Response.Message);
                if (build.TargetExam.IsLocked) return Fail("This exam is locked. Unlock it before changing assignments.");

                var result = await ApplySelectionsAsync(build, request.Filter, request.Students);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new ApiResponseDto<AtktSaveResultDto>
                {
                    Success = true,
                    Message = Describe(result),
                    Data = result
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Fail($"Failed to save assignments: {ex.Message}");
            }
        }

        public async Task<ApiResponseDto<AtktSaveResultDto>> AssignAllAsync(AtktAssignAllRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var build = await BuildAsync(request.Filter, includeAllCandidates: true);
                if (build.TargetExam == null) return Fail(build.Response.Message);
                if (build.TargetExam.IsLocked) return Fail("This exam is locked. Unlock it before changing assignments.");

                // Everyone the policy considers eligible, on every subject it allows.
                var selections = build.Contexts.Values
                    .Where(c => c.Row.Cells.Any(cell => cell.Selectable))
                    .Select(c => new AtktStudentSelectionDto
                    {
                        StdMstId = c.Student.StdMstId,
                        SubjectIds = c.Row.Cells.Where(cell => cell.Selectable).Select(cell => cell.SubjectId).ToList()
                    })
                    .ToList();

                var result = await ApplySelectionsAsync(build, request.Filter, selections);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new ApiResponseDto<AtktSaveResultDto>
                {
                    Success = true,
                    Message = Describe(result),
                    Data = result
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Fail($"Failed to assign students: {ex.Message}");
            }
        }

        private async Task<AtktSaveResultDto> ApplySelectionsAsync(
            MatrixBuild build,
            AtktMatrixRequest filter,
            List<AtktStudentSelectionDto> selections)
        {
            var policy = build.Policy;
            var result = new AtktSaveResultDto();
            var maxSubjects = policy.MaxSubjectsPerStudent.GetValueOrDefault();

            foreach (var selection in selections)
            {
                if (!build.Contexts.TryGetValue(selection.StdMstId, out var context))
                {
                    result.Skipped.Add($"{selection.StdMstId}: not an eligible candidate for this exam.");
                    continue;
                }

                // The client cannot widen what the policy allows -- re-check every tick here.
                var allowed = context.Row.Cells.Where(c => c.Selectable).Select(c => c.SubjectId).ToHashSet();
                var chosen = selection.SubjectIds.Where(allowed.Contains).Distinct().ToList();

                if (maxSubjects > 0 && chosen.Count > maxSubjects)
                {
                    result.Skipped.Add(
                        $"{context.Student.StudentId}: {chosen.Count} subjects selected, the policy allows {maxSubjects}.");
                    continue;
                }

                if (chosen.Count == 0)
                {
                    if (context.Target != null && RemoveAssignment(context, policy, result))
                    {
                        result.StudentsRemoved++;
                    }
                    continue;
                }

                var isNew = context.Target == null;
                UpsertAssignment(build, context, filter, chosen);

                if (isNew) result.StudentsAssigned++; else result.StudentsUpdated++;
                result.SubjectsRegistered += chosen.Count;
            }

            await Task.CompletedTask;
            return result;
        }

        /// <summary>
        /// Writes one MarksMaster for the target exam plus one StudentMarks row per head.
        /// Rows are matched on (SubjectId, Head) and updated in place, so re-saving the same
        /// screen cannot duplicate heads the way the legacy INSERT did.
        /// </summary>
        private void UpsertAssignment(
            MatrixBuild build,
            StudentContext context,
            AtktMatrixRequest filter,
            List<Guid> chosenSubjects)
        {
            var policy = build.Policy;
            var target = context.Target;

            if (target == null)
            {
                target = new MarksMaster
                {
                    MarksId = Guid.NewGuid(),
                    StdMstId = context.Student.StdMstId,
                    StudentID = context.Student.StudentId,
                    ExamId = filter.TargetExamId,
                    AcademicYearAYID = filter.Ayid,
                    SemesterId = filter.Semester,
                    Pattern = filter.Pattern,
                    SeatNo = policy.CarryForwardSeatNo ? context.Source?.SeatNo : null,
                    QuotaType = context.Source?.QuotaType,
                    StudentMarks = new List<StudentMarks>()
                };
                _context.MarksMasters.Add(target);
                context.Target = target;
            }
            else if (policy.CarryForwardSeatNo && string.IsNullOrWhiteSpace(target.SeatNo))
            {
                target.SeatNo = context.Source?.SeatNo;
            }

            var existing = (target.StudentMarks ?? new List<StudentMarks>()).ToList();
            var sourceHeads = context.Source?.StudentMarks?.ToList() ?? new List<StudentMarks>();
            var keep = new HashSet<Guid>();

            foreach (var column in build.Response.Columns)
            {
                var isChosen = chosenSubjects.Contains(column.SubjectId);
                var sourceForSubject = sourceHeads.Where(h => h.SubjectId == column.SubjectId).ToList();

                // A subject the student is not re-appearing for is only carried over when the
                // policy says so and there is something to carry.
                if (!isChosen && (!policy.CarryForwardMarks || sourceForSubject.Count == 0)) continue;

                build.CreditBySubject.TryGetValue(column.SubjectId, out var credit);
                var headKeys = column.Heads.Count > 0
                    ? column.Heads.Select(h => h.Head).ToList()
                    : sourceForSubject.Select(h => h.Head ?? string.Empty).Distinct().ToList();

                foreach (var headKey in headKeys)
                {
                    var sourceHead = sourceForSubject.FirstOrDefault(
                        h => string.Equals(h.Head, headKey, StringComparison.OrdinalIgnoreCase));

                    var row = existing.FirstOrDefault(
                        h => h.SubjectId == column.SubjectId &&
                             string.Equals(h.Head, headKey, StringComparison.OrdinalIgnoreCase));

                    if (row == null)
                    {
                        row = new StudentMarks
                        {
                            Id = Guid.NewGuid(),
                            MarksId = target.MarksId,
                            SubjectId = column.SubjectId,
                            CreditsId = credit?.CreditsId ?? sourceHead?.CreditsId,
                            Head = headKey
                        };
                        _context.StudentMarks.Add(row);
                        existing.Add(row);
                    }
                    else
                    {
                        row.IsDeleted = false;
                        row.DeletedAt = null;
                    }

                    keep.Add(row.Id);

                    if (isChosen)
                    {
                        // A fresh attempt: the student sits this head again, so nothing is carried.
                        row.IsCarryForward = false;
                        row.RawMarks = null;
                        row.Marks = null;
                        row.Resolution = null;
                        row.IsAbsent = false;
                        row.Grace = null;
                        row.Grade = null;
                        row.GradePoint = null;
                        row.RawGradePoint = null;
                    }
                    else
                    {
                        // Not appearing -- the legacy '+' sentinel, modelled properly.
                        row.IsCarryForward = true;
                        row.RawMarks = sourceHead?.RawMarks;
                        row.Marks = sourceHead?.Marks;
                        row.Resolution = sourceHead?.Resolution;
                        row.IsAbsent = sourceHead?.IsAbsent ?? false;
                        row.Grace = sourceHead?.Grace;
                        row.Grade = sourceHead?.Grade;
                        row.GradePoint = sourceHead?.GradePoint;
                        row.RawGradePoint = sourceHead?.RawGradePoint;
                    }
                }
            }

            // Anything left over belongs to a subject that is no longer part of this assignment.
            foreach (var orphan in existing.Where(h => !keep.Contains(h.Id) && !h.IsDeleted))
            {
                orphan.IsDeleted = true;
                orphan.DeletedAt = DateTime.UtcNow;
            }

            target.StudentMarks = existing;
        }

        private static bool RemoveAssignment(
            StudentContext context, ExamAssignmentPolicy policy, AtktSaveResultDto result)
        {
            var target = context.Target!;
            var heads = target.StudentMarks?.ToList() ?? new List<StudentMarks>();
            var marksEntered = heads.Any(h => !h.IsCarryForward && (h.Marks.HasValue || h.IsAbsent))
                               || !string.IsNullOrWhiteSpace(target.OverallRemark);

            if (policy.BlockDeleteAfterMarksEntry && marksEntered)
            {
                result.Skipped.Add($"{context.Student.StudentId}: marks already entered, assignment kept.");
                return false;
            }

            foreach (var head in heads.Where(h => !h.IsDeleted))
            {
                head.IsDeleted = true;
                head.DeletedAt = DateTime.UtcNow;
            }

            target.IsDeleted = true;
            target.DeletedAt = DateTime.UtcNow;
            context.Target = null;
            return true;
        }

        private static string Describe(AtktSaveResultDto r)
        {
            var parts = new List<string>();
            if (r.StudentsAssigned > 0) parts.Add($"{r.StudentsAssigned} assigned");
            if (r.StudentsUpdated > 0) parts.Add($"{r.StudentsUpdated} updated");
            if (r.StudentsRemoved > 0) parts.Add($"{r.StudentsRemoved} removed");
            if (parts.Count == 0) parts.Add("No changes");

            var message = string.Join(", ", parts) + ".";
            if (r.Skipped.Count > 0) message += $" {r.Skipped.Count} skipped.";
            return message;
        }

        private static ApiResponseDto<AtktSaveResultDto> Fail(string message) => new()
        {
            Success = false,
            Message = message
        };

        // =====================================================================
        // Delete
        // =====================================================================

        public async Task<ApiResponseDto<object>> DeleteAssignmentAsync(AtktDeleteRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var policy = await ResolvePolicyAsync(request.Filter.Mode, request.Filter.PolicyId, request.Filter.Pattern);

                var target = await _context.MarksMasters
                    .Include(mm => mm.StudentMarks)
                    .FirstOrDefaultAsync(mm => mm.StdMstId == request.StdMstId
                                               && mm.ExamId == request.Filter.TargetExamId
                                               && mm.AcademicYearAYID == request.Filter.Ayid
                                               && mm.SemesterId == request.Filter.Semester
                                               && mm.Pattern == request.Filter.Pattern);

                if (target == null)
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "This student is not assigned to the selected exam."
                    };
                }

                var heads = target.StudentMarks?.ToList() ?? new List<StudentMarks>();
                var marksEntered = heads.Any(h => !h.IsCarryForward && (h.Marks.HasValue || h.IsAbsent))
                                   || !string.IsNullOrWhiteSpace(target.OverallRemark);

                if (policy.BlockDeleteAfterMarksEntry && marksEntered)
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "Cannot remove this student -- marks have already been entered for the selected exam."
                    };
                }

                await _genericRepository.DeleteRangeAsync<StudentMarks>(h => h.MarksId == target.MarksId);
                await _genericRepository.DeleteAsync<MarksMaster>(target.MarksId);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new ApiResponseDto<object>
                {
                    Success = true,
                    Message = "Student removed from the selected exam."
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new ApiResponseDto<object>
                {
                    Success = false,
                    Message = $"Failed to remove student: {ex.Message}"
                };
            }
        }

        // =====================================================================
        // Excel
        // =====================================================================

        public async Task<(byte[] Content, string FileName)> ExportAsync(AtktExportRequest request)
        {
            var filter = request.Filter;
            filter.EditMode = true; // an export always describes the saved state
            var build = await BuildAsync(filter, includeAllCandidates: false);
            var seatNoOnly = string.Equals(request.ExportType, "SeatNo", StringComparison.OrdinalIgnoreCase);

            var examName = build.TargetExam?.Name ?? "Exam";
            var course = await _context.CourseMasters
                .Where(c => c.CourseId == filter.CourseId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync() ?? string.Empty;

            ExcelPackage.License.SetNonCommercialPersonal("ReactApi Project");
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add(Sanitize(examName));

            var columns = build.Response.Columns;
            var leading = seatNoOnly ? 1 : 3;
            var totalColumns = Math.Max(leading + columns.Count, 1);

            sheet.Cells[1, 1, 1, totalColumns].Merge = true;
            sheet.Cells[1, 1].Value = course.ToUpperInvariant();
            sheet.Cells[2, 1, 2, totalColumns].Merge = true;
            sheet.Cells[2, 1].Value = $"{filter.Semester} :- {examName}" +
                                      (seatNoOnly ? " (seat numbers appearing)" : string.Empty);
            sheet.Cells[1, 1, 2, totalColumns].Style.Font.Bold = true;
            sheet.Cells[1, 1, 2, totalColumns].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            const int headerRow = 4;
            if (seatNoOnly)
            {
                sheet.Cells[headerRow, 1].Value = "Seat No.";
            }
            else
            {
                sheet.Cells[headerRow, 1].Value = "Student ID";
                sheet.Cells[headerRow, 2].Value = "Seat No.";
                sheet.Cells[headerRow, 3].Value = "Student Name";
            }

            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var heads = column.Heads.Count == 0
                    ? string.Empty
                    : " [" + string.Join(", ", column.Heads.Select(h => $"{h.HeadType} {h.Pass}/{h.OutOf}")) + "]";
                sheet.Cells[headerRow, leading + 1 + i].Value = $"{column.SubjectCode} - {column.SubjectName}{heads}";
            }

            var headerRange = sheet.Cells[headerRow, 1, headerRow, totalColumns];
            headerRange.Style.Font.Bold = true;
            headerRange.Style.WrapText = true;
            headerRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            headerRange.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
            headerRange.Style.Border.BorderAround(ExcelBorderStyle.Thin);
            sheet.Row(headerRow).Height = 42;

            if (seatNoOnly)
            {
                // One compacted, top-aligned column of seat numbers per subject -- the list the
                // invigilator carries into the hall.
                for (var i = 0; i < columns.Count; i++)
                {
                    var row = headerRow + 1;
                    foreach (var student in build.Response.Students)
                    {
                        var cell = student.Cells.FirstOrDefault(c => c.SubjectId == columns[i].SubjectId);
                        if (cell?.Selected != true) continue;
                        sheet.Cells[row++, leading + 1 + i].Value = student.SeatNo ?? student.StudentId;
                    }
                }
            }
            else
            {
                var row = headerRow + 1;
                foreach (var student in build.Response.Students)
                {
                    sheet.Cells[row, 1].Value = student.StudentId;
                    sheet.Cells[row, 2].Value = student.SeatNo;
                    sheet.Cells[row, 3].Value = student.StudentName;

                    for (var i = 0; i < columns.Count; i++)
                    {
                        var cell = student.Cells.FirstOrDefault(c => c.SubjectId == columns[i].SubjectId);
                        sheet.Cells[row, leading + 1 + i].Value = cell?.Selected == true
                            ? "Applied"
                            : cell == null || cell.Status == StatusNotAttempted
                                ? string.Empty
                                : cell.ObtainedTotal.ToString();
                    }

                    row++;
                }
            }

            if (sheet.Dimension != null)
            {
                sheet.Cells[sheet.Dimension.Address].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

            if (!seatNoOnly && build.Response.Students.Count > 0)
            {
                sheet.Cells[headerRow + 1, 3, headerRow + build.Response.Students.Count, 3]
                    .Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            }

            sheet.Cells.AutoFitColumns();

            var fileName = $"{Sanitize(examName)} {(seatNoOnly ? "Seat No" : "ALL")}.xlsx";
            return (package.GetAsByteArray(), fileName);
        }

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Where(c => !invalid.Contains(c) && c != ':' && c != '/' && c != '\\').ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(cleaned)) return "Exam";
            return cleaned.Length <= 28 ? cleaned : cleaned[..28];
        }
    }
}
