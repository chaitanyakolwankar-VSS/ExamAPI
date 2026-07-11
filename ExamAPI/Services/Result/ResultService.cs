using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Result.Engine;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ExamAPI.Services.Result
{
    public class ResultService : IResultService
    {
        private readonly ApplicationDbContext _context;
        private readonly EngineRegistry _registry;
        private const string ABSENT_REMARK = "Ab";

        public ResultService(ApplicationDbContext context, EngineRegistry registry)
        {
            _context = context;
            _registry = registry;
        }

        public async Task<IEnumerable<ExamOptionDto>> GetExamsAsync(Guid branchId, string semId, string pattern, Guid collegeId)
        {
            var exams = await _context.Exams
                .Where(e => e.Course != null && e.Course.CollegeId == collegeId && e.CourseId == branchId && e.Semester == semId && !e.IsDeleted)
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
                // 1. Fetch Students who have marks entry for this exam
                var marksQuery = _context.MarksMasters
                    .Include(mm => mm.Student)
                    .Include(mm => mm.StudentMarks)
                    .Where(mm => mm.Student != null && mm.Student.CollegeId == collegeId && mm.ExamId == request.ExamId && mm.SemesterId == request.SemId && mm.Pattern == request.Pattern && !mm.IsDeleted);

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
                
                if (exam.IsLocked)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "This exam is locked. Result processing is not allowed." };
                }

                // 2. Validation: Check if all marks are entered. Absent marks are complete entries and remain failures.
                var incompleteRecords = marksRecords
                    .Where(mm => mm.StudentMarks == null || mm.StudentMarks.Any(sm => sm.Marks == null && !IsAbsentMark(sm)))
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

                // 3. Validation: Check if grace already exists (optional, based on legacy behavior)
                // In legacy: "Grace found in some of the students marks entry, Kindly update marks entry first!"
                var alreadyGraced = marksRecords.Where(mm => mm.StudentMarks.Any(sm => !string.IsNullOrEmpty(sm.Grace))).ToList();
                if (alreadyGraced.Any() && !request.IsSingleStudent) // Allow re-processing for single student
                {
                     return new ApiResponseDto<object> 
                     { 
                         Success = false, 
                         Message = "Grace already exists for some students. Please clear grace before re-processing." 
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
                    (rs.ExamType != null && NormalizeKey(rs.ExamType) == NormalizeKey(exam.ExamType)) || 
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
                        .FirstOrDefaultAsync(m => m.MarksId == mm.MarksId);

                    if (fullMm != null)
                    {
                        await ProcessStudentResult(fullMm, request, ruleSet);
                    }
                }

                await _context.SaveChangesAsync();

                return new ApiResponseDto<object> { Success = true, Message = "Results processed successfully." };
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object> { Success = false, Message = $"Error processing results: {ex.Message}" };
            }
        }

        private async Task ProcessStudentResult(MarksMaster marksMaster, ProcessResultRequest request, RuleSet ruleSet)
        {
            if (marksMaster.StudentMarks == null) return;

            foreach (var sm in marksMaster.StudentMarks)
            {
                ResetAppliedGrace(sm);
            }

            await ApplyResolutionGraceAsync(marksMaster);

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

        private void CalculateBaseGradePoints(MarksMaster marksMaster, RuleSet ruleSet)
        {
            var passingStrategyAction = ruleSet.Rules?
                .SelectMany(r => r.Actions ?? Enumerable.Empty<RuleAction>())
                .FirstOrDefault(a => string.Equals(a.ActionType, "SetPassingStrategy", StringComparison.OrdinalIgnoreCase));

            bool isCombined = string.Equals(passingStrategyAction?.Target, "Combined", StringComparison.OrdinalIgnoreCase);

            var subjectGroups = marksMaster.StudentMarks!
                .GroupBy(sm => sm.SubjectId)
                .ToList();

            bool isFail = false;

            foreach (var group in subjectGroups)
            {
                double subjectTotal = group.Sum(sm => (double)(sm.Marks ?? 0));
                double subjectOutOf = group.Sum(sm => (double)GetOutOf(sm));
                double percentage = subjectOutOf > 0 ? (subjectTotal * 100 / subjectOutOf) : 0;
                
                var (gp, gradeStr) = GetGradePointFromPercentage(percentage, ruleSet.GradeMaster);
                bool subjectPassed = false;

                if (isCombined)
                {
                    var firstHead = group.FirstOrDefault();
                    var headFormulaStr = firstHead?.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == firstHead?.Head)?.HeadFormula;
                    
                    if (!string.IsNullOrEmpty(headFormulaStr) && int.TryParse(headFormulaStr, out int formulaPercentage))
                    {
                        var requiredTotal = (subjectOutOf * formulaPercentage) / 100.0;
                        subjectPassed = subjectTotal >= requiredTotal;
                    }
                    else
                    {
                        var totalPassingMarks = group.Sum(sm => (double)GetPassingMarks(sm));
                        subjectPassed = subjectTotal >= totalPassingMarks;
                    }
                }
                else
                {
                    subjectPassed = group.All(sm => (sm.Marks ?? 0) >= GetPassingMarks(sm));
                }

                if (!subjectPassed)
                {
                    isFail = true;
                }

                foreach (var sm in group)
                {
                    sm.GradePoint = subjectPassed ? (int)gp : 0;
                    sm.Grade = gradeStr;
                }
            }

            marksMaster.OverallRemark = isFail ? "Fail" : "Pass";
        }

        private void CalculateBaseSGPI(MarksMaster marksMaster)
        {
            double totalGradePoints = 0;
            double totalCredits = 0;

            var subjectGroups = marksMaster.StudentMarks!
                .GroupBy(sm => sm.SubjectId)
                .ToList();

            foreach (var group in subjectGroups)
            {
                var firstSm = group.First();
                var creditMaster = firstSm.CreditMaster;
                if (creditMaster == null) continue;

                double subjectCredits = double.TryParse(creditMaster.TotalCredits, out var c) ? c : 0;
                var gp = firstSm.GradePoint ?? 0;
                
                totalGradePoints += gp * subjectCredits;
                totalCredits += subjectCredits;
            }

            double sgpi = totalCredits > 0 ? totalGradePoints / totalCredits : 0;
            marksMaster.SGPI = (decimal)sgpi;
        }

        private (double GradePoint, string Grade) GetGradePointFromPercentage(double percentage, GradeMaster? gradeMaster)
        {
            if (gradeMaster?.Thresholds != null && gradeMaster.Thresholds.Any())
            {
                var threshold = gradeMaster.Thresholds
                    .OrderByDescending(t => t.MinPercentage)
                    .FirstOrDefault(t => (decimal)percentage >= t.MinPercentage && (decimal)percentage <= t.MaxPercentage);
                
                if (threshold != null) return ((double)threshold.GradePoint, threshold.Grade ?? "P");
            }

            throw new InvalidOperationException("GradeMaster is not configured or no matching threshold found for the given percentage. Ensure ordinances are correctly set up.");
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

            var subjectGroups = marksMaster.StudentMarks!
                .GroupBy(sm => sm.SubjectId)
                .ToList();
            // A GradePoint of 0 means the subject is failed (calculated in CalculateBaseGradePoints)
            int failedCount = subjectGroups.Count(group => group.First().GradePoint == 0);
            overallResult.KtTheory = failedCount.ToString();
            overallResult.SGPI = marksMaster.SGPI;
            overallResult.Credits = marksMaster.StudentMarks
                .GroupBy(sm => sm.SubjectId)
                .Select(g => g.First().CreditMaster)
                .Where(cm => cm != null)
                .Sum(cm => double.TryParse(cm!.TotalCredits, out var val) ? val : 0)
                .ToString();
            
            double totalCreditsForSem = double.TryParse(overallResult.Credits, out var semCred) ? semCred : 0;
            overallResult.CreditGradePoint = ((double)(marksMaster.SGPI ?? 0) * totalCreditsForSem).ToString();

            bool hasBacklog = await _context.StudentsOverallResults
                .AnyAsync(r => r.StdMstId == marksMaster.StdMstId && 
                               string.Compare(r.SemesterId, marksMaster.SemesterId) < 0 && 
                               r.KtTheory != "0");

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

        private double GetGradePoint(StudentMarks sm, GradeMaster? gm)
        {
            int marks = sm.Marks ?? 0;
            int outOf = GetOutOf(sm);
            double percentage = outOf > 0 ? (double)marks * 100 / outOf : 0;

            return GetGradePointFromPercentage(percentage, gm).GradePoint;
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



        private bool CompareValues(double factValue, string op, string targetValueStr)
        {
            if (!double.TryParse(targetValueStr, out double targetValue)) return false;
            return op switch
            {
                "Equals" or "==" => factValue == targetValue,
                "GreaterThan" or ">" => factValue > targetValue,
                "LessThan" or "<" => factValue < targetValue,
                "GreaterOrEqual" or "GreaterThanOrEqual" or ">=" => factValue >= targetValue,
                "LessOrEqual" or "LessThanOrEqual" or "<=" => factValue <= targetValue,
                "NotEquals" or "!=" => factValue != targetValue,
                _ => false
            };
        }

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
                    .Where(mm => mm.Student != null && mm.Student.CollegeId == collegeId && mm.ExamId == request.ExamId && mm.Pattern == request.Pattern && mm.SemesterId == request.SemId && !mm.IsDeleted);

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
                    TotalMarks = mm.StudentMarks?.Sum(sm => sm.Marks ?? 0) ?? 0,
                    OutOf = mm.StudentMarks?.Sum(sm => GetOutOf(sm)) ?? 0,
                    ResultStatus = mm.OverallRemark ?? "Pending",
                    Sgpi = mm.SGPI ?? 0,
                    Cgpi = mm.CGPI ?? 0,
                    Remarks = (mm.ResultRemark == "RLE" ? "RLE, " : "") + string.Join(", ", mm.StudentMarks?.Where(sm => !string.IsNullOrEmpty(sm.Grace)).Select(sm => $"{sm.Subject?.SubjectCode}: {sm.Grace}") ?? Enumerable.Empty<string>()),
                    SubjectMarks = mm.StudentMarks?.ToDictionary(
                        sm => sm.Subject?.SubjectCode ?? sm.Id.ToString(),
                        sm => {
                            var markStr = sm.Marks?.ToString() ?? "0";
                            var symbol = sm.Grace != null ? new string(sm.Grace.Where(c => !char.IsDigit(c)).ToArray()) : "";
                            return $"{markStr}{symbol}/{GetOutOf(sm)}";
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
                return new ApiResponseDto<IEnumerable<ResultDataDto>> { Success = false, Message = $"Error fetching results: {ex.Message}" };
            }
        }

        public async Task<byte[]> ExportResultsExcelAsync(ProcessResultRequest request, Guid collegeId)
        {
            var resultsResponse = await GetResultsAsync(request, collegeId);
            if (!resultsResponse.Success || resultsResponse.Data == null) return Array.Empty<byte>();

            var results = resultsResponse.Data.ToList();
            var sb = new System.Text.StringBuilder();

            // Headers
            var subjectIds = results.SelectMany(r => r.SubjectMarks.Keys).Distinct().OrderBy(id => id).ToList();
            var headers = new List<string> { "Seat No", "Student ID", "Student Name" };
            headers.AddRange(subjectIds);
            headers.AddRange(new[] { "Total", "OutOf", "%", "Result", "Remarks" });
            sb.AppendLine(string.Join(",", headers));

            // Data
            foreach (var r in results)
            {
                var row = new List<string>
                {
                    r.SeatNo,
                    r.StudentId,
                    $"\"{r.StudentName}\"" // Quote names in case of commas
                };

                foreach (var subId in subjectIds)
                {
                    row.Add(r.SubjectMarks.ContainsKey(subId) ? r.SubjectMarks[subId] : "-");
                }

                row.Add(r.TotalMarks.ToString());
                row.Add(r.OutOf.ToString());
                row.Add(r.Percentage.ToString());
                row.Add(r.ResultStatus);
                row.Add($"\"{r.Remarks}\"");

                sb.AppendLine(string.Join(",", row));
            }

            return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        }

        private int GetPassingMarks(StudentMarks sm)
        {
             var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
             if (credit != null && int.TryParse(credit.HeadPass, out int pass)) return pass;
             throw new InvalidOperationException($"Passing marks not configured for CreditMaster head: {sm.Head}.");
        }

        private int GetOutOf(StudentMarks sm)
        {
            var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
            if (credit != null && int.TryParse(credit.HeadOutOf, out int o)) return o;
            throw new InvalidOperationException($"Maximum out of marks not configured for CreditMaster head: {sm.Head}.");
        }

        private static int GetOutOfStatic(StudentMarks sm)
        {
            var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
            if (credit != null && int.TryParse(credit.HeadOutOf, out int o)) return o;
            throw new InvalidOperationException($"Maximum out of marks not configured for CreditMaster head: {sm.Head}.");
        }

        private static void ResetAppliedGrace(StudentMarks sm)
        {
            var wasAbsent = IsAbsentMark(sm);
            
            // If RawMarks is null but Marks has a value (old data), migrate it once
            if (!sm.RawMarks.HasValue && sm.Marks.HasValue && !wasAbsent)
            {
                var previousGrace = ExtractGraceMarks(sm.Grace);
                var previousResolution = sm.Resolution ?? 0;
                sm.RawMarks = Math.Max(0, sm.Marks.Value - previousGrace - previousResolution);
            }

            sm.Marks = sm.RawMarks;
            sm.Grace = null;
            sm.Resolution = null;
            sm.Remark = wasAbsent ? ABSENT_REMARK : null;
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

        private static bool IsAbsentMark(StudentMarks sm)
        {
            return sm.Marks == null && string.Equals(sm.Remark, ABSENT_REMARK, StringComparison.OrdinalIgnoreCase);
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

        private async Task ApplyResolutionGraceAsync(MarksMaster marksMaster, string? symbol = "^")
        {
            if (marksMaster.StudentMarks == null || !marksMaster.ExamId.HasValue) return;

            var creditIds = marksMaster.StudentMarks
                .Where(sm => sm.CreditsId.HasValue)
                .Select(sm => sm.CreditsId!.Value)
                .Distinct()
                .ToList();

            var resolutionQuery = _context.Resolution
                .Where(r => r.ExamID == marksMaster.ExamId && !r.IsDeleted && r.CreditID.HasValue && creditIds.Contains(r.CreditID.Value));

            if (marksMaster.AcademicYearAYID.HasValue)
            {
                resolutionQuery = resolutionQuery.Where(r => r.AYID == marksMaster.AcademicYearAYID);
            }

            var resolutions = await resolutionQuery.ToListAsync();

            var failedSubjects = marksMaster.StudentMarks
                .Where(sm => sm.RawMarks.HasValue && string.IsNullOrEmpty(sm.Grace) && sm.RawMarks < GetPassingMarks(sm))
                .OrderBy(sm => GetPassingMarks(sm) - sm.RawMarks)
                .ToList();

            foreach (var sm in failedSubjects)
            {
                var resolution = resolutions.FirstOrDefault(r =>
                    r.CreditID == sm.CreditsId &&
                    string.Equals(r.Head, sm.Head, StringComparison.OrdinalIgnoreCase));

                if (resolution == null || !int.TryParse(resolution.Resolution, out var resolutionLimit) || resolutionLimit <= 0)
                {
                    continue;
                }

                int required = GetPassingMarks(sm) - (sm.RawMarks ?? 0);
                if (required <= resolutionLimit)
                {
                    sm.Resolution = required;
                    sm.Marks = (sm.RawMarks ?? 0) + required;
                    sm.Grace = (sm.Grace ?? "") + (symbol ?? "^");
                    sm.Remark = "Successful";
                }
            }
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
                        content.PaddingVertical(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2); // Seat No
                                columns.RelativeColumn(3); // Student ID
                                columns.RelativeColumn(4); // Student Name
                                foreach (var id in subjectIds) columns.RelativeColumn(2);
                                columns.RelativeColumn(1); // Total
                                columns.RelativeColumn(1); // OutOf
                                columns.RelativeColumn(1); // %
                                columns.RelativeColumn(1); // SGPI
                                columns.RelativeColumn(1); // CGPI
                                columns.RelativeColumn(2); // Result
                            });

                            table.Header(header =>
                            {
                                header.Cell().BorderBottom(1).Padding(2).Text("Seat No").Bold();
                                header.Cell().BorderBottom(1).Padding(2).Text("Student ID").Bold();
                                header.Cell().BorderBottom(1).Padding(2).Text("Student Name").Bold();
                                foreach (var id in subjectIds)
                                {
                                    header.Cell().BorderBottom(1).Padding(2).Text(id).Bold();
                                }
                                header.Cell().BorderBottom(1).Padding(2).Text("Total").Bold();
                                header.Cell().BorderBottom(1).Padding(2).Text("OutOf").Bold();
                                header.Cell().BorderBottom(1).Padding(2).Text("%").Bold();
                                header.Cell().BorderBottom(1).Padding(2).Text("SGPI").Bold();
                                header.Cell().BorderBottom(1).Padding(2).Text("CGPI").Bold();
                                header.Cell().BorderBottom(1).Padding(2).Text("Result").Bold();
                            });

                            foreach (var r in results)
                            {
                                table.Cell().Padding(2).Text(r.SeatNo);
                                table.Cell().Padding(2).Text(r.StudentId);
                                table.Cell().Padding(2).Text(r.StudentName);
                                foreach (var id in subjectIds)
                                {
                                    var val = r.SubjectMarks.ContainsKey(id) ? r.SubjectMarks[id] : "-";
                                    table.Cell().Padding(2).Text(val);
                                }
                                table.Cell().Padding(2).Text(r.TotalMarks.ToString());
                                table.Cell().Padding(2).Text(r.OutOf.ToString());
                                table.Cell().Padding(2).Text(r.Percentage.ToString("0.00"));
                                table.Cell().Padding(2).Text(r.Sgpi.ToString("0.00"));
                                table.Cell().Padding(2).Text(r.Cgpi.ToString("0.00"));
                                table.Cell().Padding(2).Text(r.ResultStatus);
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
