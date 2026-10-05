using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Result.Engine;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using System.Drawing;

namespace ExamAPI.Services.AtktRevalExam
{
    /// <summary>
    /// Assignment into a follow-up attempt: ATKT (backlog) or Revaluation. Replaces the legacy
    /// WebForm frm_atktreval_exm_assign.aspx.
    /// <para>
    /// Two modelling decisions carry the migration:
    /// </para>
    /// <list type="number">
    /// <item>The legacy trailing-'+' sentinel inside the h1/h2 marks strings ("not appearing for
    /// this head") is <see cref="StudentMarks.IsCarryForward"/>. False means the student sits the
    /// head again; true means the mark is carried over from the source attempt.</item>
    /// <item>Nothing about who may be assigned is hard-coded or held in a bespoke config table.
    /// It comes from the ordinance engine: a <see cref="RuleSet"/> matched on
    /// (pattern, exam type), whose rules carry an <c>AllowExamAssignment</c> action. The rule's
    /// conditions are the eligibility gate; the action's Target and MaxTargetCount are the
    /// subject/head scope and the per-student cap.</item>
    /// </list>
    /// </summary>
    public class AtktRevalExamService : IAtktRevalExamService
    {
        private readonly ApplicationDbContext _context;
        private readonly IGenericRepository _genericRepository;
        private readonly EngineRegistry _registry;
        private readonly ExamAPI.Services.Files.IFileStorage? _storage;

        public AtktRevalExamService(
            ApplicationDbContext context,
            IGenericRepository genericRepository,
            EngineRegistry registry,
            ExamAPI.Services.Files.IFileStorage? storage = null)
        {
            _context = context;
            _genericRepository = genericRepository;
            _registry = registry;
            _storage = storage;
        }

        /// <summary>A subject with no marks at all in the source attempt.</summary>
        private const string StatusNotAttempted = "NotAttempted";

        /// <summary>The action type a rule uses to grant assignment into a follow-up exam.</summary>
        public const string AllowExamAssignmentAction = "AllowExamAssignment";

        /// <summary>
        /// Revaluation exams inherit their parent's ExamType, so they cannot be told apart by it.
        /// A rule set opts in to revaluation by carrying one of these as its ExamType instead.
        /// </summary>
        private static readonly string[] RevaluationRuleSetKeys = { "REVAL", "REVALUATION" };

        /// <summary>
        /// Exam-type keys that make an exam a valid ATKT target. Exam Master authors the type as
        /// "A.T.K.T"; historical / seeded data uses "KT". Both normalise into this set so a Regular
        /// exam is never offered as an ATKT target. Revaluation targets are matched separately by
        /// their <see cref="ExamMaster.RevaluationForExamId"/> link, not by exam type.
        /// </summary>
        private static readonly HashSet<string> AtktTargetExamTypeKeys = new() { "ATKT", "KT" };

        private static bool IsAtktTargetType(string? examType) =>
            AtktTargetExamTypeKeys.Contains(HeadTargetSpec.NormalizeKey(examType));

        // =====================================================================
        // Rule resolution
        // =====================================================================

        /// <summary>One rule that grants assignment, with the scope its action declares.</summary>
        private sealed record GrantRule(Rule Rule, HeadTargetSpec Scope, int MaxSubjects);

        /// <summary>What the ordinance engine says about assignment into one target exam.</summary>
        private sealed class AssignmentRules
        {
            public RuleSet? RuleSet { get; init; }
            public List<GrantRule> Grants { get; init; } = new();
            public HeadTargetSpec FallbackScope { get; init; } = HeadTargetSpec.All;

            /// <summary>True when a rule set actually governs this exam type.</summary>
            public bool IsConfigured => RuleSet != null && Grants.Count > 0;
        }

        /// <summary>The scope granted to one student, after their conditions were evaluated.</summary>
        private sealed record StudentGrant(bool Granted, HeadTargetSpec Scope, int MaxSubjects)
        {
            public static readonly StudentGrant Denied = new(false, HeadTargetSpec.All, 0);
        }

        private async Task<AssignmentRules> ResolveRulesAsync(string pattern, ExamMaster targetExam, bool isReval)
        {
            var ruleSets = await _context.RuleSets
                .Include(rs => rs.Rules!.Where(r => r.IsEnabled && !r.IsDeleted).OrderBy(r => r.Priority))
                    .ThenInclude(r => r.Conditions)
                .Include(rs => rs.Rules!.Where(r => r.IsEnabled && !r.IsDeleted).OrderBy(r => r.Priority))
                    .ThenInclude(r => r.Actions)
                .Include(rs => rs.Pattern)
                .Where(rs => rs.Pattern!.PatternName == pattern && rs.IsActive && !rs.IsDeleted)
                .ToListAsync();

            var ruleSet = SelectRuleSet(ruleSets, targetExam, isReval);

            var grants = new List<GrantRule>();
            foreach (var rule in ruleSet?.Rules ?? Enumerable.Empty<Rule>())
            {
                var action = rule.Actions?.FirstOrDefault(a =>
                    HeadTargetSpec.NormalizeKey(a.ActionType) == HeadTargetSpec.NormalizeKey(AllowExamAssignmentAction));

                if (action == null) continue;

                grants.Add(new GrantRule(
                    rule,
                    HeadTargetSpec.Parse(action.Target),
                    Math.Max(0, action.MaxTargetCount.GetValueOrDefault())));
            }

            return new AssignmentRules
            {
                RuleSet = ruleSet,
                Grants = grants,
                FallbackScope = DefaultScope(isReval)
            };
        }

        private static RuleSet? SelectRuleSet(List<RuleSet> ruleSets, ExamMaster targetExam, bool isReval)
        {
            if (isReval)
            {
                return ruleSets.FirstOrDefault(rs =>
                    RevaluationRuleSetKeys.Contains(HeadTargetSpec.NormalizeKey(rs.ExamType)));
            }

            // Same resolution ResultService uses: match ExamType, falling back to the rule set
            // name for sets authored before ExamType existed.
            var wanted = HeadTargetSpec.NormalizeKey(targetExam.ExamType);
            if (wanted.Length == 0) return null;

            return ruleSets.FirstOrDefault(rs => HeadTargetSpec.NormalizeKey(rs.ExamType) == wanted)
                ?? ruleSets.FirstOrDefault(rs => rs.ExamType == null && HeadTargetSpec.NormalizeKey(rs.Name) == wanted);
        }

        /// <summary>
        /// What applies when no rule set carries an AllowExamAssignment action. Deliberately
        /// permissive on heads -- re-attempting every head of a chosen subject is never silently
        /// wrong, whereas guessing a head name that this college does not use would lock the
        /// whole grid. Naming heads in a rule's Target is how a college narrows it.
        /// </summary>
        private static HeadTargetSpec DefaultScope(bool isReval) =>
            isReval
                // Nothing to revalue without a mark, so absent and unattempted are out.
                ? HeadTargetSpec.ForStatuses("FAILED", "PASSED")
                // A backlog is anything not cleared.
                : HeadTargetSpec.ForStatuses("FAILED", "ABSENT", "NOTATTEMPTED");

        private async Task<StudentGrant> EvaluateGrantAsync(
            AssignmentRules rules, StudentMaster student, MarksMaster? source)
        {
            if (!rules.IsConfigured)
            {
                return new StudentGrant(true, rules.FallbackScope, 0);
            }

            // Conditions read the student's source attempt; with no attempt there is nothing to
            // evaluate and no basis to grant.
            if (source == null) return StudentGrant.Denied;

            HeadTargetSpec? combined = null;
            var maxSubjects = 0;
            var unlimited = false;
            var granted = false;

            foreach (var grant in rules.Grants)
            {
                var holds = await RuleConditionEvaluator.EvaluateRuleAsync(
                    _registry, grant.Rule, student, source, throwOnMissingFact: false);

                if (!holds) continue;

                granted = true;
                combined = combined == null ? grant.Scope : combined.Union(grant.Scope);

                if (grant.MaxSubjects <= 0) unlimited = true;
                else if (!unlimited) maxSubjects = Math.Max(maxSubjects, grant.MaxSubjects);

                if (grant.Rule.StopOnSuccess) break;
            }

            return granted
                ? new StudentGrant(true, combined ?? HeadTargetSpec.All, unlimited ? 0 : maxSubjects)
                : StudentGrant.Denied;
        }

        private static AtktPolicyDto DescribeRules(AssignmentRules rules, bool isReval)
        {
            var scope = rules.IsConfigured
                ? rules.Grants.Select(g => g.Scope).Aggregate((a, b) => a.Union(b))
                : rules.FallbackScope;

            var unlimited = !rules.IsConfigured || rules.Grants.Any(g => g.MaxSubjects <= 0);
            var cap = unlimited || rules.Grants.Count == 0 ? (int?)null : rules.Grants.Max(g => g.MaxSubjects);

            return new AtktPolicyDto
            {
                RuleSetId = rules.RuleSet?.RuleSetId ?? Guid.Empty,
                RuleSetName = rules.RuleSet?.Name ?? "Built-in default",
                ExamType = rules.RuleSet?.ExamType,
                Mode = isReval ? AssignmentModes.Revaluation : AssignmentModes.Atkt,
                IsConfigured = rules.IsConfigured,
                SubjectScopes = scope.DescribeStatuses().ToList(),
                HeadTypes = scope.DescribeHeads().ToList(),
                MaxSubjectsPerStudent = cap,
                Rules = rules.Grants.Select(g => g.Rule.Name).ToList()
            };
        }

        // =====================================================================
        // Exam pickers
        // =====================================================================

        public async Task<List<AtktExamOptionDto>> GetSourceExamsAsync(
            Guid courseId, Guid ayid, string semester, string pattern, string mode)
        {
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

            return exams.Select(ToExamOption).OrderBy(e => e.ExamName).ToList();
        }

        public async Task<List<AtktExamOptionDto>> GetTargetExamsAsync(
            Guid courseId, Guid ayid, string semester, string mode, Guid? sourceExamId)
        {
            var isReval = AssignmentModes.IsRevaluation(mode);

            var query = _context.Exams
                .Where(e => e.CourseId == courseId && e.AcademicYearAYID == ayid && e.IsActive == true);

            if (isReval)
            {
                // The revaluation target is the mirror exam of the chosen source, not a free
                // choice -- the modelled form of the legacy 'R' + exam_code convention.
                query = sourceExamId.HasValue && sourceExamId.Value != Guid.Empty
                    ? query.Where(e => e.RevaluationForExamId == sourceExamId.Value)
                    : query.Where(e => e.RevaluationForExamId != null);
            }
            else
            {
                query = query.Where(e => e.RevaluationForExamId == null);
            }

            var exams = await query.AsNoTracking().ToListAsync();
            if (isReval) return exams.Select(ToExamOption).OrderBy(e => e.ExamName).ToList();

            // An ATKT target must itself be an ATKT-type exam. A Regular exam is a source, never
            // an ATKT target, so it is excluded here. This is a filter on the exam's own type,
            // not on any RuleSet -- who/what a rule permits is still evaluated only after a
            // concrete target has been chosen.
            exams = exams
                .Where(e => IsAtktTargetType(e.ExamType))
                .ToList();

            return exams.Select(ToExamOption).OrderBy(e => e.ExamName).ToList();
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
            public AssignmentRules Rules { get; set; } = new();
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
            public HeadTargetSpec Scope { get; set; } = HeadTargetSpec.All;
            public int MaxSubjects { get; set; }
        }

        private async Task<MatrixBuild> BuildAsync(AtktMatrixRequest request, bool includeAllCandidates)
        {
            var build = new MatrixBuild();
            var response = build.Response;
            var isReval = AssignmentModes.IsRevaluation(request.Mode);

            var targetExam = await _context.Exams.FirstOrDefaultAsync(e => e.ExamId == request.TargetExamId);
            build.TargetExam = targetExam;
            if (targetExam == null)
            {
                response.Success = false;
                response.Message = "Select the exam to assign students to.";
                return build;
            }

            // The picker (GetTargetExamsAsync) only offers a mode-appropriate target; enforce the
            // same predicates here so a hand-crafted request cannot pair a mode with the wrong exam.
            if (isReval)
            {
                if (targetExam.RevaluationForExamId == null)
                {
                    build.TargetExam = null; // so Save/AssignAll treat this as a failed build
                    response.Success = false;
                    response.Message = "The selected exam is not a revaluation exam. Select the revaluation exam for the chosen source exam.";
                    return build;
                }
            }
            else if (targetExam.RevaluationForExamId != null || !IsAtktTargetType(targetExam.ExamType))
            {
                build.TargetExam = null;
                response.Success = false;
                response.Message = "The selected exam is not an ATKT exam. Select an ATKT exam to assign students to.";
                return build;
            }

            if (isReval && (request.SourceExamId == null || request.SourceExamId == Guid.Empty))
            {
                response.Success = false;
                response.Message = "Revaluation needs the source exam whose marks are being revalued.";
                return build;
            }

            // DEC-16: the revaluation reuses its parent's seat numbers, so the source must be that parent.
            if (isReval && request.SourceExamId != targetExam.RevaluationForExamId)
            {
                build.TargetExam = null;
                response.Success = false;
                response.Message = "The selected revaluation exam does not belong to the chosen source exam.";
                return build;
            }

            build.Rules = await ResolveRulesAsync(request.Pattern, targetExam, isReval);
            response.Policy = DescribeRules(build.Rules, isReval);

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
            var attemptsWithSubjectResults = resultsByMarks.Keys.ToHashSet();

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

                // Mirrors SubjectPassEvaluator's own branch, including its fallback to the sum of
                // head minimums when a combined subject has no PassPercentage configured.
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

            // ---- rows ---------------------------------------------------------------
            foreach (var student in roster.OrderBy(s => s.StudentId))
            {
                attemptsByStudent.TryGetValue(student.StdMstId, out var studentAttempts);
                studentAttempts ??= new List<MarksMaster>();

                var target = studentAttempts.FirstOrDefault(a => a.ExamId == request.TargetExamId);
                var source = isReval
                    ? studentAttempts.FirstOrDefault(a => a.ExamId == request.SourceExamId)
                    : ResolveAtktSourceAttempt(
                        studentAttempts,
                        request.TargetExamId,
                        attemptsWithSubjectResults);

                var isAssigned = target != null;

                if (!includeAllCandidates)
                {
                    if (request.EditMode && !isAssigned) continue;
                    if (!request.EditMode && isAssigned) continue;
                }

                if (source == null && target == null) continue;

                var grant = await EvaluateGrantAsync(build.Rules, student, source);

                // An already-assigned student stays visible even if the rules would no longer
                // grant them, so the operator can still see and undo the assignment.
                if (!grant.Granted && !isAssigned) continue;

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
                    SourceSelectionReason = isReval
                        ? "Selected revaluation source exam."
                        : source == null
                            ? null
                            : "Latest valid completed attempt; target exam excluded.",
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
                        cell.Deficit = Math.Max(0, cell.RequiredToPass - computed.ObtainedTotal);
                        cell.IsAbsent = computed.SubjectStatus == SubjectStatuses.Absent;
                    }
                    else
                    {
                        // The single authority on pass/fail. It branches on this subject's own
                        // PassingStrategy, so a combined subject is judged on its total and a
                        // head-wise one on every head, inside the same grid.
                        var verdict = SubjectPassEvaluator.Evaluate(group);
                        cell.ObtainedTotal = verdict.ObtainedTotal;
                        cell.OutOfTotal = verdict.OutOfTotal;
                        cell.RequiredToPass = verdict.RequiredToPass;
                        cell.Deficit = verdict.Deficit;
                        cell.IsAbsent = verdict.IsAllAbsent;
                        cell.Status = verdict.IsAllAbsent
                            ? SubjectStatuses.Absent
                            : verdict.IsPassed ? SubjectStatuses.Passed : SubjectStatuses.Failed;
                    }

                    ApplySelectability(cell, column, grant.Scope);

                    cell.Selected = isAssigned
                        ? targetHeads.Any(h => h.SubjectId == column.SubjectId && !h.IsCarryForward)
                        // Revaluation is always an explicit choice; a backlog list is not.
                        : cell.Selectable && !isReval && IsBacklog(cell.Status);

                    // Per-head marks + selection -- the head-wise surface. The tile view reads this
                    // to show both head marks and offer a checkbox per head; the matrix view can
                    // read the same list. Combined subjects carry heads too but select all-or-nothing.
                    cell.PassingStrategy = column.PassingStrategy;
                    var isCombined = string.Equals(column.PassingStrategy, PassingStrategies.Combined,
                        StringComparison.OrdinalIgnoreCase);

                    cell.Heads = group
                        .OrderBy(h => h.Head)
                        .Select(h =>
                        {
                            var headFailing = !h.IsAbsent && !SubjectPassEvaluator.IsHeadPassed(h);
                            // A head may be re-sat only when the subject is selectable AND the rule's
                            // head scope permits this head. With no rule (or an unrestricted Target)
                            // every head qualifies; a Target naming e.g. "ESE" locks the others so an
                            // internal-assessment head carries forward instead of being re-examined.
                            var headSelectable = cell.Selectable && grant.Scope.MatchesHead(h);
                            return new AtktCellHeadDto
                            {
                                Head = h.Head ?? string.Empty,
                                HeadType = SubjectPassEvaluator.GetHeadLabel(h),
                                Obtained = h.Marks,
                                OutOf = SubjectPassEvaluator.GetHeadOutOf(h),
                                Pass = SubjectPassEvaluator.GetHeadPass(h),
                                IsAbsent = h.IsAbsent,
                                IsFailing = headFailing,
                                Selectable = headSelectable,
                                Selected = isAssigned
                                    ? targetHeads.Any(t => t.SubjectId == column.SubjectId
                                        && t.Head == h.Head && !t.IsCarryForward)
                                    // New head-wise default re-sits only the failing/absent heads
                                    // that are in scope; combined follows the whole-subject choice.
                                    : headSelectable && !isReval
                                      && (isCombined ? cell.Selected : (h.IsAbsent || headFailing))
                            };
                        })
                        .ToList();

                    row.Cells.Add(cell);
                }

                row.BacklogCount = row.Cells.Count(c =>
                    c.Status == SubjectStatuses.Failed || c.Status == SubjectStatuses.Absent);

                if (isAssigned)
                {
                    var marksEntered = targetHeads.Any(h => !h.IsCarryForward && (h.Marks.HasValue || h.IsAbsent))
                                       || !string.IsNullOrWhiteSpace(target!.OverallRemark);
                    row.CanDelete = !marksEntered;
                    row.DeleteBlockedReason = marksEntered
                        ? "Marks have already been entered for this student in the selected exam."
                        : null;
                }

                // Nothing outstanding and nothing assigned means no reason to be listed.
                if (!includeAllCandidates && !request.EditMode && !row.Cells.Any(c => c.Selectable)) continue;

                build.Contexts[student.StdMstId] = new StudentContext
                {
                    Student = student,
                    Source = source,
                    Target = target,
                    Row = row,
                    Scope = grant.Scope,
                    MaxSubjects = grant.MaxSubjects
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

        /// <summary>
        /// Decides whether the operator may tick a cell, entirely from the rule action's scope.
        /// </summary>
        private static void ApplySelectability(AtktCellDto cell, AtktSubjectColumnDto column, HeadTargetSpec scope)
        {
            if (column.Heads.Count == 0)
            {
                cell.Selectable = false;
                cell.Reason = "No heads are configured for this subject.";
                return;
            }

            if (scope.RestrictsHeads && !column.Heads.Any(h => scope.MatchesHead(h.Head, h.HeadType)))
            {
                cell.Selectable = false;
                cell.Reason = $"Only {string.Join(", ", scope.DescribeHeads())} can be applied for.";
                return;
            }

            if (!scope.MatchesStatus(cell.Status))
            {
                cell.Selectable = false;
                cell.Reason = cell.Status switch
                {
                    SubjectStatuses.Passed => "Already cleared.",
                    SubjectStatuses.Absent => "Marked absent -- outside the scope of this exam.",
                    StatusNotAttempted => "No marks recorded in the source exam.",
                    _ => "Outside the scope of this exam."
                };
                return;
            }

            cell.Selectable = true;
        }

        private static bool IsBacklog(string status) =>
            status == SubjectStatuses.Failed || status == SubjectStatuses.Absent || status == StatusNotAttempted;

        /// <summary>
        /// Selects the source for a new ATKT assignment. Source resolution is intentionally kept
        /// in one place so a blank assignment can never become a student's next source attempt.
        /// ExamMaster has no session date or attempt sequence yet, so exam creation chronology is
        /// the best available ordering and MarksMaster.CreatedAt is only a deterministic tie-breaker.
        /// </summary>
        private static MarksMaster? ResolveAtktSourceAttempt(
            IEnumerable<MarksMaster> attempts,
            Guid targetExamId,
            ISet<Guid> attemptsWithSubjectResults)
        {
            return attempts
                .Where(attempt => attempt.ExamId != targetExamId)
                .Where(attempt => HasSourceEvidence(attempt, attemptsWithSubjectResults))
                .OrderByDescending(attempt => attempt.Exam?.CreatedAt ?? DateTime.MinValue)
                .ThenByDescending(attempt => attempt.CreatedAt)
                .ThenByDescending(attempt => attempt.MarksId)
                .FirstOrDefault();
        }

        /// <summary>
        /// A target registration with only blank fresh heads is not a completed source attempt.
        /// A saved subject result, mark, raw mark, or absence is sufficient evidence that the
        /// attempt may drive the next ATKT decision.
        /// </summary>
        private static bool HasSourceEvidence(MarksMaster attempt, ISet<Guid> attemptsWithSubjectResults)
        {
            if (attemptsWithSubjectResults.Contains(attempt.MarksId)) return true;

            return attempt.StudentMarks?.Any(head => !head.IsDeleted &&
                (head.Marks.HasValue || head.RawMarks.HasValue || head.IsAbsent)) == true;
        }

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

                var result = ApplySelections(build, request.Filter, request.Students);

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
                return Fail($"Failed to save assignments: {ExamAPI.Services.Common.SafeError.Message(ex)}");
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

                // Everyone the rules grant, on every subject those rules allow.
                var selections = build.Contexts.Values
                    .Where(c => c.Row.Cells.Any(cell => cell.Selectable))
                    .Select(c => new AtktStudentSelectionDto
                    {
                        StdMstId = c.Student.StdMstId,
                        SubjectIds = c.Row.Cells.Where(cell => cell.Selectable).Select(cell => cell.SubjectId).ToList()
                    })
                    .ToList();

                var result = ApplySelections(build, request.Filter, selections);

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
                return Fail($"Failed to assign students: {ExamAPI.Services.Common.SafeError.Message(ex)}");
            }
        }

        private AtktSaveResultDto ApplySelections(
            MatrixBuild build,
            AtktMatrixRequest filter,
            List<AtktStudentSelectionDto> selections)
        {
            var result = new AtktSaveResultDto();

            foreach (var selection in selections)
            {
                if (!build.Contexts.TryGetValue(selection.StdMstId, out var context))
                {
                    result.Skipped.Add($"{selection.StdMstId}: not an eligible candidate for this exam.");
                    continue;
                }

                // The client cannot widen what the rules allow -- re-check every tick here.
                var allowed = context.Row.Cells.Where(c => c.Selectable).Select(c => c.SubjectId).ToHashSet();

                // Merge the two selection surfaces: subject-level (SubjectIds, from the matrix view
                // and combined subjects) and head-level (Subjects, from the tile view). A null value
                // means every head of the subject; a set means only those heads are re-sat.
                var chosenHeads = new Dictionary<Guid, HashSet<string>?>();
                foreach (var subjectId in selection.SubjectIds)
                {
                    if (allowed.Contains(subjectId)) chosenHeads[subjectId] = null;
                }
                foreach (var subj in selection.Subjects)
                {
                    if (!allowed.Contains(subj.SubjectId)) continue;
                    chosenHeads[subj.SubjectId] = subj.Heads == null || subj.Heads.Count == 0
                        ? null
                        : new HashSet<string>(subj.Heads, StringComparer.OrdinalIgnoreCase);
                }

                var chosen = chosenHeads.Keys.ToList();

                if (context.MaxSubjects > 0 && chosen.Count > context.MaxSubjects)
                {
                    result.Skipped.Add(
                        $"{context.Student.StudentId}: {chosen.Count} subjects selected, the rule allows {context.MaxSubjects}.");
                    continue;
                }

                if (chosen.Count == 0)
                {
                    if (context.Target != null && RemoveAssignment(context, result)) result.StudentsRemoved++;
                    continue;
                }

                var isNew = context.Target == null;
                UpsertAssignment(build, context, filter, chosenHeads);

                if (isNew) result.StudentsAssigned++; else result.StudentsUpdated++;
                result.SubjectsRegistered += chosen.Count;
            }

            return result;
        }

        /// <summary>
        /// Writes one MarksMaster for the target exam plus one StudentMarks row per head.
        /// <para>
        /// Rows are matched on (SubjectId, Head) and updated in place, so re-saving the same
        /// screen cannot duplicate heads the way the legacy INSERT did.
        /// </para>
        /// <para>
        /// Only heads inside the granted scope are blanked for a fresh attempt. Every other head
        /// -- of a chosen subject whose scope names specific heads, or of a subject the student
        /// is not re-attempting at all -- carries its marks over. That is what makes the legacy
        /// "re-sit the theory paper, keep the term work" behaviour expressible, and it is load
        /// bearing for combined passing: the subject verdict is the sum across heads, so a
        /// blanked carry-forward head would silently drop the total and fail the student on data
        /// loss rather than on performance.
        /// </para>
        /// </summary>
        private void UpsertAssignment(
            MatrixBuild build,
            StudentContext context,
            AtktMatrixRequest filter,
            Dictionary<Guid, HashSet<string>?> chosenHeads)
        {
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
                    SeatNo = context.Source?.SeatNo,
                    QuotaType = context.Source?.QuotaType,
                    StudentMarks = new List<StudentMarks>()
                };
                _context.MarksMasters.Add(target);
                context.Target = target;
            }
            else if (AssignmentModes.IsRevaluation(filter.Mode))
            {
                // DEC-16: a revaluation is only a paper re-check, so it reuses the parent exam's
                // seat number rather than getting its own. Keep the row in step with the parent
                // on every save (the Seat No screen does not list revaluation exams).
                if (!string.IsNullOrWhiteSpace(context.Source?.SeatNo))
                {
                    target.SeatNo = context.Source!.SeatNo;
                }
                if (string.IsNullOrWhiteSpace(target.QuotaType))
                {
                    target.QuotaType = context.Source?.QuotaType;
                }
            }
            else if (string.IsNullOrWhiteSpace(target.SeatNo))
            {
                target.SeatNo = context.Source?.SeatNo;
            }

            var existing = (target.StudentMarks ?? new List<StudentMarks>()).ToList();
            var sourceHeads = context.Source?.StudentMarks?.ToList() ?? new List<StudentMarks>();
            var keep = new HashSet<Guid>();

            foreach (var column in build.Response.Columns)
            {
                var isChosen = chosenHeads.TryGetValue(column.SubjectId, out var pickedHeads);
                var sourceForSubject = sourceHeads.Where(h => h.SubjectId == column.SubjectId).ToList();

                // A subject the student is not re-appearing for is carried over only when there
                // is something to carry.
                if (!isChosen && sourceForSubject.Count == 0) continue;

                build.CreditBySubject.TryGetValue(column.SubjectId, out var credit);

                var headKeys = column.Heads.Count > 0
                    ? column.Heads.Select(h => h.Head).ToList()
                    : sourceForSubject.Select(h => h.Head ?? string.Empty).Distinct().ToList();

                foreach (var headKey in headKeys)
                {
                    var headType = column.Heads.FirstOrDefault(h =>
                        string.Equals(h.Head, headKey, StringComparison.OrdinalIgnoreCase))?.HeadType;

                    var sourceHead = sourceForSubject.FirstOrDefault(
                        h => string.Equals(h.Head, headKey, StringComparison.OrdinalIgnoreCase));

                    // A head is re-sat only when the subject is chosen, the operator picked this
                    // head (null pickedHeads = every head, e.g. combined or matrix subject-level),
                    // and the rule scope also permits it. Unpicked heads fall through and carry over.
                    var headPicked = pickedHeads == null
                        || pickedHeads.Contains(headKey)
                        || (!string.IsNullOrEmpty(headType) && pickedHeads.Contains(headType));
                    var isReattempt = isChosen && headPicked && context.Scope.MatchesHead(headKey, headType);

                    // Nothing to write: not re-sat and no prior mark to preserve.
                    if (!isReattempt && sourceHead == null) continue;

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

                    if (isReattempt)
                    {
                        // A fresh attempt: the student sits this head again, so nothing carries.
                        row.IsCarryForward = false;
                        row.RawMarks = null;
                        row.Marks = null;
                        row.Resolution = null;
                        row.IsAbsent = false;
                        row.Grace = null;
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
                    }
                }
            }

            // Anything left over belongs to a subject no longer part of this assignment.
            foreach (var orphan in existing.Where(h => !keep.Contains(h.Id) && !h.IsDeleted))
            {
                orphan.IsDeleted = true;
                orphan.DeletedAt = DateTime.UtcNow;
            }

            target.StudentMarks = existing;
        }

        /// <summary>
        /// Unassigns a student. Refused once any mark has been entered for the attempt -- the
        /// legacy guard, which is an invariant rather than something a college may switch off.
        /// </summary>
        private static bool RemoveAssignment(StudentContext context, AtktSaveResultDto result)
        {
            var target = context.Target!;
            var heads = target.StudentMarks?.ToList() ?? new List<StudentMarks>();
            var marksEntered = heads.Any(h => !h.IsCarryForward && (h.Marks.HasValue || h.IsAbsent))
                               || !string.IsNullOrWhiteSpace(target.OverallRemark);

            if (marksEntered)
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
                var targetExam = await _context.Exams
                    .FirstOrDefaultAsync(e => e.ExamId == request.Filter.TargetExamId);
                if (targetExam != null && targetExam.IsLocked)
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = "This exam is locked. Unlock it before changing assignments."
                    };
                }

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

                if (marksEntered)
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
                    Message = $"Failed to remove student: {ExamAPI.Services.Common.SafeError.Message(ex)}"
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

            // College name/logo from College Details (DEC-14). No tenant (platform admin) = no branding.
            var branding = _context.CurrentCollegeId is Guid brandingCollegeId
                ? await ExamAPI.Services.Report.CollegeBranding.LoadAsync(_context, _storage, brandingCollegeId)
                : ExamAPI.Services.Report.CollegeBrandingInfo.Empty;

            var columns = build.Response.Columns;
            var students = build.Response.Students;

            var headerFill = XLColor.FromArgb(0xE5, 0xE7, 0xEB); // light grey
            var zebraFill = XLColor.FromArgb(0xF7, 0xF8, 0xFA);  // near-white
            var appliedFill = XLColor.FromArgb(0xDC, 0xFC, 0xE7); // light green
            var titleColor = XLColor.FromArgb(0x11, 0x18, 0x27);

            using var workbook = ExamAPI.Services.Report.ExcelStyles.NewWorkbook();
            var sheet = workbook.Worksheets.Add(Sanitize(examName));
            ExamAPI.Services.Report.ExcelStyles.NormalMargins(sheet);

            // The heads a student re-sits for a subject: the head labels for combined/head-wise,
            // "Yes" when the subject has no distinct heads.
            static string AppliedHeads(AtktCellDto? cell)
            {
                if (cell == null || !cell.Selected) return string.Empty;
                var heads = cell.Heads.Where(h => h.Selected).Select(h => h.HeadType)
                    .Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
                return heads.Count > 0 ? string.Join(", ", heads) : "Yes";
            }

            const int headerRow = 4;

            if (seatNoOnly)
            {
                var totalCols = Math.Max(columns.Count, 1);
                WriteTitle(sheet, totalCols, course, $"{filter.Semester} — {examName} · seat numbers appearing", titleColor, branding, textStartCol: 2);

                var maxRows = 0;
                for (var i = 0; i < columns.Count; i++)
                {
                    var seats = students
                        .Where(s => s.Cells.FirstOrDefault(c => c.SubjectId == columns[i].SubjectId)?.Selected == true)
                        .Select(s => s.SeatNo ?? s.StudentId)
                        .ToList();
                    sheet.Cell(headerRow, i + 1).Value = $"{columns[i].SubjectCode}\n{columns[i].SubjectName}\n({seats.Count})";
                    var r = headerRow + 1;
                    foreach (var seat in seats) sheet.Cell(r++, i + 1).Value = seat;
                    maxRows = Math.Max(maxRows, seats.Count);
                }

                StyleHeader(sheet.Range(headerRow, 1, headerRow, totalCols), headerFill);
                sheet.Row(headerRow).Height = 80;

                var lastRow = headerRow + Math.Max(maxRows, 1);
                var body = sheet.Range(headerRow, 1, lastRow, totalCols);
                Grid(body);
                body.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                sheet.SheetView.FreezeRows(headerRow);
                for (var c = 1; c <= totalCols; c++) ExamAPI.Services.Report.ExcelStyles.SetWidth(sheet.Column(c), 20);
                AddBanner(sheet, totalCols, titleColor, branding);
                ApplyPrintSetup(sheet, headerRow);
            }
            else
            {
                const int leading = 4;               // Sr, Student ID, Seat No, Student Name
                var totalCols = leading + columns.Count + 1; // + Applied
                WriteTitle(sheet, totalCols, course, $"{filter.Semester} — {examName}", titleColor, branding, textStartCol: 3);

                sheet.Cell(headerRow, 1).Value = "Sr.";
                sheet.Cell(headerRow, 2).Value = "Student ID";
                sheet.Cell(headerRow, 3).Value = "Seat No.";
                sheet.Cell(headerRow, 4).Value = "Student Name";
                for (var i = 0; i < columns.Count; i++)
                    sheet.Cell(headerRow, leading + 1 + i).Value = $"{columns[i].SubjectCode}\n{columns[i].SubjectName}";
                sheet.Cell(headerRow, totalCols).Value = "Applied";

                StyleHeader(sheet.Range(headerRow, 1, headerRow, totalCols), headerFill);
                sheet.Row(headerRow).Height = 80;

                var row = headerRow + 1;
                var sr = 1;
                foreach (var s in students)
                {
                    sheet.Cell(row, 1).Value = sr++;
                    sheet.Cell(row, 2).Value = s.StudentId;
                    sheet.Cell(row, 3).Value = s.SeatNo;
                    sheet.Cell(row, 4).Value = s.StudentName;

                    if ((row - headerRow) % 2 == 0)
                        sheet.Range(row, 1, row, leading).Style.Fill.BackgroundColor = zebraFill;

                    var applied = 0;
                    for (var i = 0; i < columns.Count; i++)
                    {
                        var cell = s.Cells.FirstOrDefault(c => c.SubjectId == columns[i].SubjectId);
                        var text = AppliedHeads(cell);
                        var target = sheet.Cell(row, leading + 1 + i);
                        target.Value = text;
                        if (text.Length > 0)
                        {
                            applied++;
                            target.Style.Fill.BackgroundColor = appliedFill;
                        }
                    }
                    sheet.Cell(row, totalCols).Value = applied;
                    row++;
                }

                var lastRow = Math.Max(row - 1, headerRow);
                var body = sheet.Range(headerRow, 1, lastRow, totalCols);
                Grid(body);
                body.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                if (row - 1 >= headerRow + 1)
                    sheet.Range(headerRow + 1, 4, lastRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                sheet.SheetView.Freeze(headerRow, leading);
                ExamAPI.Services.Report.ExcelStyles.SetWidth(sheet.Column(1), 5);
                ExamAPI.Services.Report.ExcelStyles.SetWidth(sheet.Column(2), 12);
                ExamAPI.Services.Report.ExcelStyles.SetWidth(sheet.Column(3), 10);
                ExamAPI.Services.Report.ExcelStyles.SetWidth(sheet.Column(4), 38);
                for (var i = 0; i < columns.Count; i++) ExamAPI.Services.Report.ExcelStyles.SetWidth(sheet.Column(leading + 1 + i), 20);
                ExamAPI.Services.Report.ExcelStyles.SetWidth(sheet.Column(totalCols), 9);
                AddBanner(sheet, totalCols, titleColor, branding);
                ApplyPrintSetup(sheet, headerRow);
            }

            var fileName = $"{Sanitize(examName)} {(seatNoOnly ? "Seat No" : "ALL")}.xlsx";
            return (ExamAPI.Services.Report.ExcelStyles.ToBytes(workbook), fileName);
        }

        private static void ApplyPrintSetup(IXLWorksheet sheet, int headerRow)
        {
            var ps = sheet.PageSetup;
            ps.PageOrientation = XLPageOrientation.Landscape;
            ps.PaperSize = XLPaperSize.A4Paper;
            ps.FitToPages(1, 0);
            ps.SetRowsToRepeatAtTop(headerRow, headerRow);
        }

        // Rows 1-3 carry the title block: row 1 is the college banner (or, without one, the college
        // name when known), then the course and the semester/exam line. The table header stays on
        // row 4. With a banner the picture is added by AddBanner once the column widths are known
        // and the name text is not printed; otherwise the logo floats top-left and the text is
        // merged from textStartCol onward to keep clear of it.
        private static void WriteTitle(IXLWorksheet sheet, int totalCols, string course, string subtitle, XLColor titleColor, ExamAPI.Services.Report.CollegeBrandingInfo? branding = null, int textStartCol = 1)
        {
            var hasBanner = branding?.HasBanner == true;
            var hasLogo = !hasBanner && branding?.Logo is { Length: > 0 };
            var startCol = hasLogo && totalCols > textStartCol ? textStartCol : 1;

            var lines = new List<(string Text, float Size, double Height)>();
            if (hasBanner) lines.Add((string.Empty, 14, 60)); // banner row; resized by AddBanner
            else if (!string.IsNullOrWhiteSpace(branding?.Name)) lines.Add((branding!.Name, 14, 22));
            lines.Add((course.ToUpperInvariant(), 13, 20));
            lines.Add((subtitle, 11, 18));

            for (var i = 0; i < lines.Count; i++)
            {
                var row = i + 1;
                var rowStartCol = hasBanner && i == 0 ? 1 : startCol;
                sheet.Range(row, rowStartCol, row, totalCols).Merge();
                var cell = sheet.Cell(row, rowStartCol);
                cell.Value = lines[i].Text.Length == 0 ? Blank.Value : lines[i].Text;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = lines[i].Size;
                cell.Style.Font.FontColor = titleColor;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                sheet.Row(row).Height = lines[i].Height;
            }

            if (branding != null && !hasBanner)
                ExamAPI.Services.Report.ExcelBranding.TryAddLogo(sheet, branding.Logo, 1, 1, 80, 44);
        }

        /// <summary>
        /// Draws the banner over row 1 of the title block once the column widths are final. If the
        /// picture cannot be embedded the college name is printed there instead, so row 1 is never empty.
        /// </summary>
        private static void AddBanner(IXLWorksheet sheet, int totalCols, XLColor titleColor, ExamAPI.Services.Report.CollegeBrandingInfo? branding)
        {
            if (branding?.HasBanner != true) return;
            if (ExamAPI.Services.Report.ExcelBranding.TryAddBanner(sheet, branding.Banner, 1, totalCols, 1) > 0) return;

            var cell = sheet.Cell(1, 1);
            cell.Value = branding.Name;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 14;
            cell.Style.Font.FontColor = titleColor;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Row(1).Height = 22;
        }

        private static void StyleHeader(IXLRange range, XLColor fill)
        {
            range.Style.Font.Bold = true;
            range.Style.Alignment.WrapText = true;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            range.Style.Fill.BackgroundColor = fill;
        }

        private static void Grid(IXLRange range)
        {
            ExamAPI.Services.Report.ExcelStyles.ThinBorders(range.Style);
        }

        private static string Sanitize(string name)
        {
            // Used for both the sheet name and the file name. Excel forbids [ ] * ? : / \ in a
            // sheet name, caps it at 31 characters and disallows a leading/trailing apostrophe.
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name
                .Where(c => !invalid.Contains(c) && "[]*?:/\\".IndexOf(c) < 0)
                .ToArray()).Trim().Trim('\'').Trim();
            if (string.IsNullOrWhiteSpace(cleaned)) return "Exam";
            // 28 leaves room for the " ALL" / " Seat No" file-name suffix and is under Excel's 31.
            return cleaned.Length <= 28 ? cleaned : cleaned[..28].TrimEnd();
        }
    }
}
