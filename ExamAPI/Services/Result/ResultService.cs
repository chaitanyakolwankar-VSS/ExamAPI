using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Lookup;
using ExamAPI.Services.Result.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace ExamAPI.Services.Result
{
    public class ResultService : IResultService
    {
        private readonly ApplicationDbContext _context;
        private readonly EngineRegistry _registry;
        private readonly ILogger<ResultService>? _logger;
        private const string FAIL_GRADE = "F";

        public ResultService(ApplicationDbContext context, EngineRegistry registry, ILogger<ResultService>? logger = null)
        {
            _context = context;
            _registry = registry;
            _logger = logger;
        }

        /// <summary>
        /// Exams of a course (optionally one academic year) for Apply Grace Marks / result processing.
        /// <paramref name="semId"/> and <paramref name="pattern"/> are accepted for compatibility but
        /// IGNORED: ExamMaster.Semester is never written (Exam Master creates exams without one), so
        /// filtering on it hid every exam (T-25). The semester is chosen separately by the caller.
        /// </summary>
        public async Task<IEnumerable<ExamOptionDto>> GetExamsAsync(Guid branchId, string semId, string pattern, Guid collegeId, Guid? ayid = null)
        {
            // With an academic year this is exactly ExamPurposes.Process (course + AY); without one it is
            // every exam of the course. The explicit college check stays as defence in depth.
            var query = ayid.HasValue && ayid.Value != Guid.Empty
                ? ExamPurposes.Query(_context, ExamPurposes.Process, branchId, ayid.Value, null)!
                : _context.Exams.Where(e => e.CourseId == branchId);

            query = query.Where(e => e.Course != null && e.Course.CollegeId == collegeId && !e.IsDeleted);

            var exams = await query
                .Select(e => new ExamOptionDto
                {
                    ExamId = e.ExamId,
                    ExamCode = e.ExamId.ToString(),
                    ExamName = e.Name
                })
                .ToListAsync();

            return exams;
        }

        public async Task<ApiResponseDto<object>> ProcessResultsAsync(ProcessResultRequest request, Guid collegeId)
        {
            try
            {
                // 1. Fetch Students who have marks entry for this exam and match the requested branch
                var marksQuery = _context.MarksMasters
                    .Include(mm => mm.Student)
                    .Include(mm => mm.StudentMarks)
                    .Include(mm => mm.Exam)
                    .Where(mm => mm.Student != null
                        && mm.Student.CollegeId == collegeId
                        && mm.ExamId == request.ExamId
                        && mm.SemesterId == request.SemId
                        && mm.Pattern == request.Pattern
                        && mm.Exam != null
                        && mm.Exam.CourseId == request.BranchId
                        && !mm.IsDeleted);

                if (request.IsSingleStudent && !string.IsNullOrEmpty(request.StudentId))
                {
                    marksQuery = marksQuery.Where(mm => mm.StudentID == request.StudentId);
                }

                var marksRecords = await marksQuery.ToListAsync();

                if (!marksRecords.Any())
                {
                    return new ApiResponseDto<object> { Success = false, Message = "No marks records found for the selected criteria." };
                }

                var exam = await _context.Exams
                    .FirstOrDefaultAsync(e => e.ExamId == request.ExamId && !e.IsDeleted);

                if (exam == null)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "Selected exam was not found." };
                }

                if (exam.CourseId != request.BranchId)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "Selected exam does not belong to the requested branch." };
                }

                if (exam.IsLocked)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "This exam is locked. Result processing is not allowed." };
                }

                // 2. Validation: Check if all marks are entered. Absent marks are complete entries and remain failures.
                var incompleteRecords = marksRecords
                    .Where(mm => mm.StudentMarks == null || mm.StudentMarks.Any(sm => sm.Marks == null && !sm.IsAbsent))
                    .ToList();
                if (incompleteRecords.Any())
                {
                    var studentIds = string.Join(", ", incompleteRecords.Take(5).Select(mm => mm.StudentID));
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = $"Marks entry incomplete for some students (e.g., {studentIds}). Kindly complete marks entry first."
                    };
                }

                // 4. Fetch RuleSet. Link via ExamType field, falling back to name matching for backward compatibility.
                var ruleSets = await _context.RuleSets
                    .Include(rs => rs.Rules!.Where(r => r.IsEnabled).OrderBy(r => r.Priority))
                        .ThenInclude(r => r.Conditions)
                    .Include(rs => rs.Rules!.Where(r => r.IsEnabled).OrderBy(r => r.Priority))
                        .ThenInclude(r => r.Actions)
                    .Include(rs => rs.Pattern)
                    .Include(rs => rs.GradeMaster)
                        .ThenInclude(gm => gm!.Thresholds)
                    .Where(rs => rs.Pattern!.PatternName == request.Pattern && rs.IsActive && !rs.IsDeleted)
                    .ToListAsync();

                var ruleSet = ruleSets.FirstOrDefault(rs =>
                    (rs.ExamType != null && ExamTypeKeys.Canonical(rs.ExamType) == ExamTypeKeys.Canonical(exam.ExamType)) ||
                    (rs.ExamType == null && IsRuleSetForExamType(rs.Name, exam.ExamType))
                );

                if (ruleSet == null)
                {
                    return new ApiResponseDto<object>
                    {
                        Success = false,
                        Message = $"No active rule set found for pattern '{request.Pattern}' and exam type '{exam.ExamType}'. Ensure a RuleSet is configured for this Exam Type."
                    };
                }

                // Resolution limits are loaded ONCE per exam. ResolutionMaster is the only source of
                // truth for '^': every run re-derives it from these, so a lowered limit revokes on
                // the next run and a first-time limit applies on the first.
                var resolutionRows = await _context.Resolution
                    .Where(r => r.ExamID == request.ExamId && !r.IsDeleted)
                    .ToListAsync();
                var resolutionLimits = ResolutionDerivation.ParseLimits(resolutionRows);

                // 5. Process each student
                foreach (var mm in marksRecords)
                {
                    // Reload with full inclusions for processing
                    var fullMm = await _context.MarksMasters
                        .Include(m => m.Student)
                        .Include(m => m.StudentMarks)
                            .ThenInclude(sm => sm.Subject)
                        .Include(m => m.StudentMarks)
                            .ThenInclude(sm => sm.CreditMaster)
                                .ThenInclude(cm => cm.Credits)
                        .Include(m => m.SubjectResults)
                        .AsSplitQuery()
                        .FirstOrDefaultAsync(m => m.MarksId == mm.MarksId);

                    if (fullMm != null)
                    {
                        await ProcessStudentResult(fullMm, request, ruleSet, resolutionLimits);
                    }
                }

                await _context.SaveChangesAsync();

                return new ApiResponseDto<object> { Success = true, Message = "Results processed successfully." };
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object> { Success = false, Message = $"Error processing results: {ExamAPI.Services.Common.SafeError.Message(ex)}" };
            }
        }

        private async Task ProcessStudentResult(MarksMaster marksMaster, ProcessResultRequest request, RuleSet ruleSet,
            IReadOnlyDictionary<Guid, int> resolutionLimits)
        {
            if (marksMaster.StudentMarks == null) return;

            // Back to what staff typed: marks = raw, no resolution, no grace symbol.
            foreach (var sm in marksMaster.StudentMarks)
            {
                ResetAppliedGrace(sm);
            }

            // Resolution ('^') is derived from ResolutionMaster BEFORE the rule phases, so ordinance
            // grace is computed from the post-resolution deficit and skips resolved heads/subjects.
            foreach (var subjectHeads in marksMaster.StudentMarks.GroupBy(sm => sm.SubjectId))
            {
                ResolutionDerivation.Apply(subjectHeads, resolutionLimits);
            }

            // Ordinance symbols are re-derived from the rules on every run, so clear the
            // previous verdict first. AddBonusSGPIHandler/SetResultRemarkHandler APPEND to
            // ResultRemark, so without this a reprocess accumulates ("#" -> "# #" -> "# # #").
            marksMaster.ResultRemark = null;

            // The subject rows must exist before Phase 1b so a combined-subject grace award has
            // somewhere to land; Phase 2 then fills in the totals and the verdict.
            EnsureSubjectResults(marksMaster);

            // Phase 1: Rule Evaluation & Sorting Actions
            var pendingActions = new List<(Rule Rule, RuleAction Action)>();
            foreach (var rule in ruleSet.Rules)
            {
                if (await EvaluateRule(marksMaster.Student, marksMaster, rule))
                {
                    foreach (var action in rule.Actions)
                    {
                        pendingActions.Add((rule, action));
                    }
                    if (rule.StopOnSuccess) break;
                }
            }

            // Phase 1b: Pre-calculation Handlers (e.g. AddGrace)
            foreach (var (rule, action) in pendingActions.Where(x => x.Action.ActionType != "DowngradeGP" && x.Action.ActionType != "AddBonusSGPI" && x.Action.ActionType != "SetResult"))
            {
                var handler = _registry.GetActionHandler(action.ActionType);
                if (handler != null)
                {
                    await handler.ExecuteAsync(marksMaster, action, rule.OrdinanceSymbol);
                }
            }

            // Phase 2: Calculate Base GradePoints
            CalculateBaseGradePoints(marksMaster, ruleSet);

            // Phase 3: Downgrade GP Handlers
            foreach (var (rule, action) in pendingActions.Where(x => x.Action.ActionType == "DowngradeGP"))
            {
                var handler = _registry.GetActionHandler(action.ActionType);
                if (handler != null)
                {
                    await handler.ExecuteAsync(marksMaster, action, rule.OrdinanceSymbol);
                }
            }

            // Phase 4: Calculate Base SGPI
            CalculateBaseSGPI(marksMaster);

            // Phase 5: Bonus SGPI Handlers
            foreach (var (rule, action) in pendingActions.Where(x => x.Action.ActionType == "AddBonusSGPI"))
            {
                var handler = _registry.GetActionHandler(action.ActionType);
                if (handler != null)
                {
                    await handler.ExecuteAsync(marksMaster, action, rule.OrdinanceSymbol);
                }
            }

            // Phase 6: Finalize Results
            foreach (var (rule, action) in pendingActions.Where(x => x.Action.ActionType == "SetResult"))
            {
                var handler = _registry.GetActionHandler(action.ActionType);
                if (handler != null)
                {
                    await handler.ExecuteAsync(marksMaster, action, rule.OrdinanceSymbol);
                }
            }

            marksMaster.SGPI = (decimal)Math.Round((double)(marksMaster.SGPI ?? 0), 2);
            await UpdateAcademicRecord(marksMaster);
        }

        /// <summary>
        /// Creates the missing StudentSubjectResult rows for this student and clears the grace
        /// from the previous run, so the pipeline always has one row per subject to write to.
        /// </summary>
        private void EnsureSubjectResults(MarksMaster marksMaster)
        {
            marksMaster.SubjectResults ??= new List<StudentSubjectResult>();

            // A subject that no longer has any StudentMarks heads (removed from the exam or the
            // assignment) must stop counting toward SGPI, credits and the backlog count.
            var liveSubjectIds = marksMaster.StudentMarks!
                .Where(sm => sm.SubjectId.HasValue)
                .Select(sm => sm.SubjectId!.Value)
                .ToHashSet();
            foreach (var orphan in marksMaster.SubjectResults.Where(r => !liveSubjectIds.Contains(r.SubjectId)).ToList())
            {
                marksMaster.SubjectResults.Remove(orphan);
                _context.StudentSubjectResults.Remove(orphan);
            }

            foreach (var group in marksMaster.StudentMarks!.GroupBy(sm => sm.SubjectId))
            {
                if (group.Key is not Guid subjectId) continue;

                var subjectResult = marksMaster.SubjectResults.FirstOrDefault(r => r.SubjectId == subjectId);
                if (subjectResult == null)
                {
                    subjectResult = new StudentSubjectResult
                    {
                        Id = Guid.NewGuid(),
                        MarksId = marksMaster.MarksId,
                        SubjectId = subjectId
                    };
                    marksMaster.SubjectResults.Add(subjectResult);
                    _context.StudentSubjectResults.Add(subjectResult);
                }

                // Carry the loaded credit config across so SGPI can read TotalCredits without
                // a second include -- rows reloaded from the DB come back without it.
                var firstHead = group.First();
                subjectResult.CreditsId = firstHead.CreditsId;
                subjectResult.CreditMaster = firstHead.CreditMaster;
                subjectResult.GraceApplied = 0;
                subjectResult.GraceSymbol = null;
            }
        }

        private void CalculateBaseGradePoints(MarksMaster marksMaster, RuleSet ruleSet)
        {
            bool isFail = false;

            foreach (var group in marksMaster.StudentMarks!.GroupBy(sm => sm.SubjectId))
            {
                var subjectResult = marksMaster.SubjectResults!.FirstOrDefault(r => r.SubjectId == group.Key);
                if (subjectResult == null) continue;

                var verdict = SubjectPassEvaluator.Evaluate(group);

                // Head-wise grace is already folded into Marks; combined grace is awarded against
                // the subject total, so it is added here.
                var obtainedTotal = verdict.ObtainedTotal + subjectResult.GraceApplied;
                var subjectPassed = verdict.IsCombined
                    ? obtainedTotal >= verdict.RequiredToPass
                    : verdict.IsPassed;

                double percentage = verdict.OutOfTotal > 0 ? (obtainedTotal * 100.0 / verdict.OutOfTotal) : 0;

                // A failed subject is graded F / 0 without a grade-table lookup, so a hole in the
                // table can never abort the batch on behalf of a subject that needs no grade.
                var (gp, gradeStr) = subjectPassed
                    ? GetGradePointFromPercentage(percentage, ruleSet.GradeMaster)
                    : (0d, FAIL_GRADE);

                subjectResult.ObtainedTotal = obtainedTotal;
                subjectResult.RawObtainedTotal = verdict.RawObtainedTotal;
                subjectResult.OutOfTotal = verdict.OutOfTotal;
                // A failed subject carries the fail grade, not its percentage band -- otherwise the
                // reports that detect failure via Grade == "F" disagree with IsPassed.
                subjectResult.Grade = subjectPassed ? gradeStr : FAIL_GRADE;
                subjectResult.GradePoint = subjectPassed ? (int)gp : 0;
                subjectResult.RawGradePoint = subjectResult.GradePoint;
                subjectResult.IsPassed = subjectPassed;
                subjectResult.SubjectStatus = verdict.IsAllAbsent
                    ? SubjectStatuses.Absent
                    : subjectPassed ? SubjectStatuses.Passed : SubjectStatuses.Failed;

                if (!subjectPassed)
                {
                    isFail = true;
                }
            }

            marksMaster.OverallRemark = isFail ? OverallRemarks.Fail : OverallRemarks.Pass;
        }

        private void CalculateBaseSGPI(MarksMaster marksMaster)
        {
            double totalGradePoints = 0;
            double totalCredits = 0;

            foreach (var subjectResult in marksMaster.SubjectResults!)
            {
                double subjectCredits = double.TryParse(subjectResult.CreditMaster?.TotalCredits, out var c) ? c : 0;

                totalGradePoints += subjectResult.GradePoint * subjectCredits;
                totalCredits += subjectCredits;
            }

            double sgpi = totalCredits > 0 ? totalGradePoints / totalCredits : 0;
            marksMaster.SGPI = (decimal)sgpi;
        }

        private (double GradePoint, string Grade) GetGradePointFromPercentage(double percentage, GradeMaster? gradeMaster)
        {
            if (gradeMaster?.Thresholds != null && gradeMaster.Thresholds.Any())
            {
                var value = (decimal)percentage;
                var ordered = gradeMaster.Thresholds.OrderByDescending(t => t.MinPercentage).ToList();

                var threshold = ordered.FirstOrDefault(t => value >= t.MinPercentage && value <= t.MaxPercentage);
                if (threshold != null) return ((double)threshold.GradePoint, threshold.Grade ?? "P");

                // The percentage fell in a gap between two bands (e.g. 39.995 between 39.99 and 40).
                // That is a grade-table data problem, not a reason to abort every student: use the
                // nearest band below it (or the lowest band when it is under all of them).
                var nearest = ordered.FirstOrDefault(t => value >= t.MinPercentage) ?? ordered.Last();
                _logger?.LogWarning(
                    "Percentage {Percentage} matches no grade threshold in GradeMaster {GradeMasterId}; using the nearest lower band {Grade} ({Min}-{Max}). Check the grade table for gaps.",
                    percentage, gradeMaster.GradeMasterId, nearest.Grade, nearest.MinPercentage, nearest.MaxPercentage);
                return ((double)nearest.GradePoint, nearest.Grade ?? "P");
            }

            throw new InvalidOperationException("GradeMaster is not configured or has no thresholds. Ensure ordinances are correctly set up.");
        }

        private async Task UpdateAcademicRecord(MarksMaster marksMaster)
        {
            var overallResult = await _context.StudentsOverallResults
                .FirstOrDefaultAsync(r => r.StdMstId == marksMaster.StdMstId && r.SemesterId == marksMaster.SemesterId);

            if (overallResult == null)
            {
                overallResult = new StudentsOverallResult
                {
                    Id = Guid.NewGuid(),
                    StdMstId = marksMaster.StdMstId,
                    SemesterId = marksMaster.SemesterId
                };
                _context.StudentsOverallResults.Add(overallResult);
            }

            int failedCount = marksMaster.SubjectResults!.Count(r => !r.IsPassed);
            overallResult.KtTheory = failedCount.ToString();
            overallResult.SGPI = marksMaster.SGPI;
            overallResult.Credits = marksMaster.SubjectResults
                .Sum(r => double.TryParse(r.CreditMaster?.TotalCredits, out var val) ? val : 0)
                .ToString();

            double totalCreditsForSem = double.TryParse(overallResult.Credits, out var semCred) ? semCred : 0;
            overallResult.CreditGradePoint = ((double)(marksMaster.SGPI ?? 0) * totalCreditsForSem).ToString();

            // Compared client-side: SemesterId is a string ("Sem-6"), and a SQL string compare
            // orders "Sem-10" before "Sem-9". A student has only a handful of prior semesters.
            var backlogSemesters = await _context.StudentsOverallResults
                .Where(r => r.StdMstId == marksMaster.StdMstId && r.KtTheory != "0")
                .Select(r => r.SemesterId)
                .ToListAsync();
            bool hasBacklog = backlogSemesters.Any(s => s != null && marksMaster.SemesterId != null && SemesterIds.Compare(s, marksMaster.SemesterId) < 0);

            if (hasBacklog)
            {
                marksMaster.ResultRemark = "RLE";
                marksMaster.CGPI = null;
            }
            else
            {
                var allSems = await _context.StudentsOverallResults
                    .Where(r => r.StdMstId == marksMaster.StdMstId && r.SGPI.HasValue)
                    .ToListAsync();

                decimal totalEarnedGradePoints = 0;
                decimal totalCreditsAllSems = 0;

                foreach (var sem in allSems)
                {
                    if (decimal.TryParse(sem.CreditGradePoint, out var cgp)) totalEarnedGradePoints += cgp;
                    if (decimal.TryParse(sem.Credits, out var c)) totalCreditsAllSems += c;
                }

                if (totalCreditsAllSems > 0)
                {
                    marksMaster.CGPI = Math.Round(totalEarnedGradePoints / totalCreditsAllSems, 2);
                }
                else
                {
                    marksMaster.CGPI = marksMaster.SGPI;
                }

                overallResult.CGPI = marksMaster.CGPI;
            }
        }

        /// <summary>
        /// The grace/ordinance notes shown beside a result: head-level symbols from StudentMarks
        /// plus the subject-level award a combined subject records on StudentSubjectResult.
        /// </summary>
        private static string BuildResultRemarks(MarksMaster mm)
        {
            var notes = new List<string>();

            if (mm.ResultRemark == "RLE") notes.Add("RLE");

            notes.AddRange(mm.StudentMarks?
                .Where(sm => !string.IsNullOrEmpty(sm.Grace))
                .Select(sm => $"{sm.Subject?.SubjectCode}: {sm.Grace}") ?? Enumerable.Empty<string>());

            notes.AddRange(mm.SubjectResults?
                .Where(r => r.GraceApplied > 0)
                .Select(r => $"{r.Subject?.SubjectCode}: {r.GraceApplied}{r.GraceSymbol}") ?? Enumerable.Empty<string>());

            return string.Join(", ", notes);
        }

        private async Task<bool> EvaluateRule(StudentMaster student, MarksMaster marksMaster, Rule rule)
        {
            foreach (var condition in rule.Conditions)
            {
                var provider = _registry.GetFactProvider(condition.FactName);
                if (provider == null)
                {
                    throw new InvalidOperationException($"Fact provider for '{condition.FactName}' not found. Rule evaluation aborted.");
                }

                double factValue = await provider.GetValueAsync(student, marksMaster);
                if (!CompareValues(factValue, condition.Operator, condition.Value))
                {
                    return false;
                }
            }
            return true;
        }



        // Operator vocabulary is shared with exam-assignment eligibility screening --
        // see RuleConditionEvaluator, which owns the comparison.
        private bool CompareValues(double factValue, string op, string targetValueStr)
            => Engine.RuleConditionEvaluator.Compare(factValue, op, targetValueStr);

        public async Task<ApiResponseDto<IEnumerable<ResultDataDto>>> GetResultsAsync(ProcessResultRequest request, Guid collegeId)
        {
            try
            {
                var query = _context.MarksMasters
                    .Include(mm => mm.Student)
                    .Include(mm => mm.StudentMarks)
                        .ThenInclude(sm => sm.Subject)
                    .Include(mm => mm.StudentMarks)
                        .ThenInclude(sm => sm.CreditMaster)
                            .ThenInclude(cm => cm.Credits)
                    .Include(mm => mm.SubjectResults)
                        .ThenInclude(r => r.Subject)
                    .Include(mm => mm.Exam)
                    .AsSplitQuery()
                    .Where(mm => mm.Student != null
                        && mm.Student.CollegeId == collegeId
                        && mm.ExamId == request.ExamId
                        && mm.Pattern == request.Pattern
                        && mm.SemesterId == request.SemId
                        && mm.Exam != null
                        && mm.Exam.CourseId == request.BranchId
                        && !mm.IsDeleted);

                if (request.IsSingleStudent && !string.IsNullOrEmpty(request.StudentId))
                {
                    query = query.Where(mm => mm.StudentID == request.StudentId);
                }

                var marksRecords = await query.ToListAsync();

                var results = marksRecords.Select(mm => new ResultDataDto
                {
                    StudentId = mm.StudentID ?? mm.Student?.StudentId ?? "N/A",
                    StudentName = mm.Student != null ? $"{mm.Student.FirstName} {mm.Student.LastName}" : "N/A",
                    SeatNo = mm.SeatNo ?? "N/A",
                    // Combined grace is awarded against the subject total, not onto a head, so it
                    // has to be added back here for the grid to agree with the marksheet.
                    TotalMarks = (mm.StudentMarks?.Sum(sm => sm.Marks ?? 0) ?? 0)
                                 + (mm.SubjectResults?.Sum(r => r.GraceApplied) ?? 0),
                    OutOf = mm.StudentMarks?.Sum(SubjectPassEvaluator.GetHeadOutOf) ?? 0,
                    ResultStatus = mm.OverallRemark ?? "Pending",
                    Sgpi = mm.SGPI ?? 0,
                    Cgpi = mm.CGPI ?? 0,
                    Remarks = BuildResultRemarks(mm),
                    SubjectMarks = mm.StudentMarks?.ToDictionary(
                        sm => $"{(sm.Subject?.SubjectCode ?? sm.Id.ToString())} - {sm.Subject?.Name ?? "Unknown"}{(string.IsNullOrEmpty(sm.Head) ? string.Empty : $" ({SubjectPassEvaluator.GetHeadLabel(sm)})")}",
                        sm => {
                            var markStr = sm.IsAbsent ? "AB" : sm.Marks?.ToString() ?? "0";
                            var symbol = sm.Grace != null ? new string(sm.Grace.Where(c => !char.IsDigit(c)).ToArray()) : "";
                            return $"{markStr}{symbol}/{SubjectPassEvaluator.GetHeadOutOf(sm)}";
                        }
                    ) ?? new Dictionary<string, string>()
                }).ToList();

                foreach(var r in results)
                {
                    if (r.OutOf > 0)
                        r.Percentage = Math.Round((r.TotalMarks / r.OutOf) * 100, 2);
                }

                return new ApiResponseDto<IEnumerable<ResultDataDto>> { Success = true, Data = results };
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<IEnumerable<ResultDataDto>> { Success = false, Message = $"Error fetching results: {ExamAPI.Services.Common.SafeError.Message(ex)}" };
            }
        }

        public async Task<byte[]> ExportResultsExcelAsync(ProcessResultRequest request, Guid collegeId)
        {
            var resultsResponse = await GetResultsAsync(request, collegeId);
            if (!resultsResponse.Success || resultsResponse.Data == null) return Array.Empty<byte>();

            var results = resultsResponse.Data.ToList();

            // Gather all subject heads
            var keys = new HashSet<string>();
            foreach (var r in results)
            {
                foreach (var k in r.SubjectMarks.Keys)
                {
                    keys.Add(k);
                }
            }

            var subjectHeads = keys.Select(k => {
                var match = System.Text.RegularExpressions.Regex.Match(k, @"^(.+?)\s*\((.+?)\)$");
                if (match.Success)
                {
                    return new { Key = k, Subject = match.Groups[1].Value, Head = match.Groups[2].Value };
                }
                return new { Key = k, Subject = k, Head = "" };
            }).OrderBy(x => x.Subject).ThenBy(x => x.Head).ToList();

            var subjectsGrouped = subjectHeads.GroupBy(x => x.Subject)
                .Select(g => new { Subject = g.Key, Heads = g.ToList() })
                .ToList();

            ExcelPackage.License.SetNonCommercialPersonal("ReactApi Project");
            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("Results");
                ws.View.ShowGridLines = true;

                // Border styles
                var thinBorder = ExcelBorderStyle.Thin;
                var borderColor = System.Drawing.Color.Gray;

                // Helper to apply border to a cell or range
                Action<ExcelRange> applyBorders = (range) => {
                    range.Style.Border.Top.Style = thinBorder;
                    range.Style.Border.Bottom.Style = thinBorder;
                    range.Style.Border.Left.Style = thinBorder;
                    range.Style.Border.Right.Style = thinBorder;
                    range.Style.Border.Top.Color.SetColor(borderColor);
                    range.Style.Border.Bottom.Color.SetColor(borderColor);
                    range.Style.Border.Left.Color.SetColor(borderColor);
                    range.Style.Border.Right.Color.SetColor(borderColor);
                };

                // Base headers
                ws.Cells["A1:A2"].Merge = true;
                ws.Cells["A1"].Value = "Seat No";
                ws.Cells["B1:B2"].Merge = true;
                ws.Cells["B1"].Value = "Student ID";
                ws.Cells["C1:C2"].Merge = true;
                ws.Cells["C1"].Value = "Student Name";

                int col = 4;
                foreach (var group in subjectsGrouped)
                {
                    int startCol = col;
                    int endCol = col + group.Heads.Count - 1;

                    if (startCol == endCol)
                    {
                        ws.Cells[1, startCol].Value = group.Subject;
                    }
                    else
                    {
                        ws.Cells[1, startCol, 1, endCol].Merge = true;
                        ws.Cells[1, startCol].Value = group.Subject;
                    }

                    for (int i = 0; i < group.Heads.Count; i++)
                    {
                        ws.Cells[2, col + i].Value = string.IsNullOrEmpty(group.Heads[i].Head) ? "-" : group.Heads[i].Head;
                    }
                    col += group.Heads.Count;
                }

                // Footer headers
                ws.Cells[1, col, 2, col].Merge = true;
                ws.Cells[1, col].Value = "Total";

                ws.Cells[1, col + 1, 2, col + 1].Merge = true;
                ws.Cells[1, col + 1].Value = "%";

                ws.Cells[1, col + 2, 2, col + 2].Merge = true;
                ws.Cells[1, col + 2].Value = "SGPI";

                ws.Cells[1, col + 3, 2, col + 3].Merge = true;
                ws.Cells[1, col + 3].Value = "CGPI";

                ws.Cells[1, col + 4, 2, col + 4].Merge = true;
                ws.Cells[1, col + 4].Value = "Result";

                ws.Cells[1, col + 5, 2, col + 5].Merge = true;
                ws.Cells[1, col + 5].Value = "Remarks";

                int totalCols = col + 5;

                // Style the header cells (bold, centered, with borders, no fill colors)
                var headerRange = ws.Cells[1, 1, 2, totalCols];
                headerRange.Style.Font.Bold = true;
                headerRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                headerRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                // Set borders for all header cells individually
                for (int r = 1; r <= 2; r++)
                {
                    for (int c = 1; c <= totalCols; c++)
                    {
                        applyBorders(ws.Cells[r, c]);
                    }
                }

                // Write rows
                int rowIdx = 3;
                foreach (var r in results)
                {
                    ws.Cells[rowIdx, 1].Value = r.SeatNo;
                    ws.Cells[rowIdx, 2].Value = r.StudentId;
                    ws.Cells[rowIdx, 3].Value = r.StudentName;
                    ws.Cells[rowIdx, 3].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;

                    int cIdx = 4;
                    foreach (var sh in subjectHeads)
                    {
                        ws.Cells[rowIdx, cIdx].Value = r.SubjectMarks.TryGetValue(sh.Key, out var val) ? val : "-";
                        ws.Cells[rowIdx, cIdx].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        cIdx++;
                    }

                    ws.Cells[rowIdx, cIdx].Value = r.TotalMarks;
                    ws.Cells[rowIdx, cIdx + 1].Value = (double)(r.Percentage / 100.0m);
                    ws.Cells[rowIdx, cIdx + 1].Style.Numberformat.Format = "0.00%";
                    ws.Cells[rowIdx, cIdx + 2].Value = r.Sgpi;
                    ws.Cells[rowIdx, cIdx + 3].Value = r.Cgpi;
                    ws.Cells[rowIdx, cIdx + 4].Value = r.ResultStatus;
                    ws.Cells[rowIdx, cIdx + 5].Value = r.Remarks;

                    // Apply horizontal alignment
                    ws.Cells[rowIdx, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    ws.Cells[rowIdx, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    ws.Cells[rowIdx, cIdx].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    ws.Cells[rowIdx, cIdx + 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    ws.Cells[rowIdx, cIdx + 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    ws.Cells[rowIdx, cIdx + 3].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    ws.Cells[rowIdx, cIdx + 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    ws.Cells[rowIdx, cIdx + 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;

                    // Apply borders
                    for (int c = 1; c <= totalCols; c++)
                    {
                        applyBorders(ws.Cells[rowIdx, c]);
                    }

                    rowIdx++;
                }

                // Autofit columns
                ws.Cells[1, 1, rowIdx - 1, totalCols].AutoFitColumns();

                // Add padding to column widths
                for (int c = 1; c <= totalCols; c++)
                {
                    ws.Column(c).Width = ws.Column(c).Width + 3;
                }

                return package.GetAsByteArray();
            }
        }

        private static void ResetAppliedGrace(StudentMarks sm)
        {
            // If RawMarks is null but Marks has a value (old data), migrate it once
            if (!sm.RawMarks.HasValue && sm.Marks.HasValue && !sm.IsAbsent)
            {
                var previousGrace = ExtractGraceMarks(sm.Grace);
                var previousResolution = sm.Resolution ?? 0;
                sm.RawMarks = Math.Max(0, sm.Marks.Value - previousGrace - previousResolution);
            }

            // Both ordinance grace and resolution are derived on every run: resolution from
            // ResolutionMaster (see ResolutionDerivation), grace from the rules.
            ResolutionDerivation.ResetToRaw(sm);
        }

        private static int ExtractGraceMarks(string? grace)
        {
            if (string.IsNullOrWhiteSpace(grace)) return 0;

            var total = 0;
            var current = string.Empty;
            foreach (var ch in grace)
            {
                if (char.IsDigit(ch))
                {
                    current += ch;
                    continue;
                }

                if (current.Length > 0 && int.TryParse(current, out var value))
                {
                    total += value;
                    current = string.Empty;
                }
            }

            if (current.Length > 0 && int.TryParse(current, out var finalValue))
            {
                total += finalValue;
            }

            return total;
        }

        private static bool IsRuleSetForExamType(string? ruleSetName, string? examType)
        {
            var ruleSetKey = NormalizeKey(ruleSetName);
            var examTypeKey = NormalizeKey(examType);

            if (string.IsNullOrWhiteSpace(ruleSetKey) || string.IsNullOrWhiteSpace(examTypeKey))
            {
                return false;
            }

            return ruleSetKey == examTypeKey || ruleSetKey.StartsWith(examTypeKey);
        }

        private static string NormalizeKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        }

        public async Task<byte[]> ExportResultsPdfAsync(ProcessResultRequest request, Guid collegeId)
        {
            var resultsResponse = await GetResultsAsync(request, collegeId);
            if (!resultsResponse.Success || resultsResponse.Data == null) return Array.Empty<byte>();

            var results = resultsResponse.Data.ToList();
            if (!results.Any()) return Array.Empty<byte>();

            var subjectIds = results.SelectMany(r => r.SubjectMarks.Keys).Distinct().OrderBy(id => id).ToList();

            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(20, Unit.Point);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Arial));

                    page.Header().Element(header =>
                    {
                        header.AlignCenter().Text("Overall Result Report").FontSize(16).SemiBold();
                    });

                    page.Content().Element(content =>
                    {
                        var headerStyle = TextStyle.Default.FontSize(7).Bold().FontColor(Colors.Black);
                        var cellStyle = TextStyle.Default.FontSize(7).FontColor(Colors.Black);
                        var pdfBorderColor = QuestPDF.Infrastructure.Color.FromHex("#CBD5E1");
                        var pdfHeaderBgColor = QuestPDF.Infrastructure.Color.FromHex("#F1F5F9");

                        Action<IContainer, string, bool, string> writeCell = (cellContainer, text, isHeader, align) =>
                        {
                            var cell = cellContainer
                                .Border(0.5f)
                                .BorderColor(pdfBorderColor)
                                .PaddingVertical(4)
                                .PaddingHorizontal(3);

                            if (isHeader)
                            {
                                cell = cell.Background(pdfHeaderBgColor);
                            }

                            if (align == "center")
                            {
                                cell.AlignCenter().Text(text).Style(isHeader ? headerStyle : cellStyle);
                            }
                            else if (align == "right")
                            {
                                cell.AlignRight().Text(text).Style(isHeader ? headerStyle : cellStyle);
                            }
                            else
                            {
                                cell.AlignLeft().Text(text).Style(isHeader ? headerStyle : cellStyle);
                            }
                        };

                        content.PaddingVertical(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.8f); // Seat No
                                columns.RelativeColumn(2.2f); // Student ID
                                columns.RelativeColumn(4.0f); // Student Name
                                foreach (var id in subjectIds) columns.RelativeColumn(1.5f); // Subject columns
                                columns.RelativeColumn(1.2f); // Total
                                columns.RelativeColumn(1.2f); // OutOf
                                columns.RelativeColumn(1.2f); // %
                                columns.RelativeColumn(1.0f); // SGPI
                                columns.RelativeColumn(1.0f); // CGPI
                                columns.RelativeColumn(1.6f); // Result
                            });

                            table.Header(header =>
                            {
                                writeCell(header.Cell(), "Seat No", true, "center");
                                writeCell(header.Cell(), "Student ID", true, "center");
                                writeCell(header.Cell(), "Student Name", true, "left");
                                foreach (var id in subjectIds)
                                {
                                    // Wrap subject codes by changing space before head to newline
                                    var displayId = id.Replace(" (", "\n(");
                                    writeCell(header.Cell(), displayId, true, "center");
                                }
                                writeCell(header.Cell(), "Total", true, "center");
                                writeCell(header.Cell(), "OutOf", true, "center");
                                writeCell(header.Cell(), "%", true, "center");
                                writeCell(header.Cell(), "SGPI", true, "center");
                                writeCell(header.Cell(), "CGPI", true, "center");
                                writeCell(header.Cell(), "Result", true, "center");
                            });

                            foreach (var r in results)
                            {
                                writeCell(table.Cell(), r.SeatNo, false, "center");
                                writeCell(table.Cell(), r.StudentId, false, "center");
                                writeCell(table.Cell(), r.StudentName, false, "left");
                                foreach (var id in subjectIds)
                                {
                                    var val = r.SubjectMarks.ContainsKey(id) ? r.SubjectMarks[id] : "-";
                                    writeCell(table.Cell(), val, false, "center");
                                }
                                writeCell(table.Cell(), r.TotalMarks.ToString(), false, "center");
                                writeCell(table.Cell(), r.OutOf.ToString(), false, "center");
                                writeCell(table.Cell(), r.Percentage.ToString("0.00"), false, "center");
                                writeCell(table.Cell(), r.Sgpi.ToString("0.00"), false, "center");
                                writeCell(table.Cell(), r.Cgpi.ToString("0.00"), false, "center");
                                writeCell(table.Cell(), r.ResultStatus, false, "center");
                            }
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
                });
            });

            return pdf.GeneratePdf();
        }
    }
}
