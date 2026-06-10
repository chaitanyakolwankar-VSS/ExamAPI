using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Result.Engine;
using Microsoft.EntityFrameworkCore;
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

        public async Task<IEnumerable<ExamOptionDto>> GetExamsAsync(Guid branchId, string semId, string pattern)
        {
            var exams = await _context.Exams
                .Where(e => e.CourseId == branchId && e.Semester == semId && !e.IsDeleted)
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
                    .Where(mm => mm.ExamId == request.ExamId && mm.SemesterId == request.SemId && mm.Pattern == request.Pattern && !mm.IsDeleted);

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

            foreach (var rule in ruleSet.Rules)
            {
                if (await EvaluateRule(marksMaster.Student, marksMaster, rule))
                {
                    await ApplyRuleActions(marksMaster, rule);
                    if (rule.StopOnSuccess) break;
                }
            }

            await CalculateFinalStatus(marksMaster, request, ruleSet);
        }

        private async Task CalculateFinalStatus(MarksMaster marksMaster, ProcessResultRequest request, RuleSet ruleSet)
        {
            var isFail = marksMaster.StudentMarks!.Any(sm => (sm.Marks ?? 0) < GetPassingMarks(sm));
            marksMaster.OverallRemark = isFail ? "Fail" : "Pass";

            // 1. SGPI Calculation - Group by Subject
            double totalGradePoints = 0;
            double totalCredits = 0;

            // Identify DowngradeGP actions
            var downgradeActions = ruleSet.Rules!
                .Where(r => r.IsEnabled)
                .SelectMany(r => r.Actions)
                .Where(a => a.ActionType == "DowngradeGP")
                .ToList();

            var subjectGroups = marksMaster.StudentMarks
                .GroupBy(sm => sm.SubjectId)
                .ToList();

            foreach (var group in subjectGroups)
            {
                var firstSm = group.First();
                var creditMaster = firstSm.CreditMaster;
                if (creditMaster == null) continue;

                double subjectTotal = group.Sum(sm => (double)(sm.Marks ?? 0));
                double subjectOutOf = group.Sum(sm => (double)GetOutOf(sm));
                double percentage = subjectOutOf > 0 ? (subjectTotal * 100 / subjectOutOf) : 0;
                
                double gp = GetGradePointFromPercentage(percentage, ruleSet.GradeMaster);

                // Apply Downgrade if it's a carry-forward subject or based on specific rule conditions
                if (gp >= 4) 
                {
                    foreach (var action in downgradeActions)
                    {
                        var rule = ruleSet.Rules!.First(r => r.Actions.Contains(action));
                        bool applies = false;

                        if (action.Target == "Subject" && group.Any(sm => sm.IsCarryForward))
                        {
                            applies = true;
                        }
                        else if (await EvaluateRule(marksMaster.Student, marksMaster, rule))
                        {
                            applies = true;
                        }

                        if (applies)
                        {
                            double downgradeVal = (double)(action.Param1Value ?? 1);
                            gp = Math.Max(4, gp - downgradeVal);
                            break; 
                        }
                    }
                }

                double subjectCredits = double.TryParse(creditMaster.TotalCredits, out var c) ? c : 0;
                totalGradePoints += gp * subjectCredits;
                totalCredits += subjectCredits;
            }

            double sgpi = totalCredits > 0 ? totalGradePoints / totalCredits : 0;

            // 2. SGPI Bonus Rules
            foreach (var rule in ruleSet.Rules!.Where(r => r.IsEnabled && r.Actions.Any(a => a.ActionType == "AddBonusSGPI")))
            {
                if (await EvaluateRule(marksMaster.Student, marksMaster, rule))
                {
                    var bonusAction = rule.Actions.First(a => a.ActionType == "AddBonusSGPI");
                    sgpi += (double)(bonusAction.Param1Value ?? 0);
                }
            }

            marksMaster.SGPI = (decimal)Math.Min(10, Math.Round(sgpi, 2));

            // 3. Update StudentsOverallResult & RLE Check
            await UpdateAcademicRecord(marksMaster);
        }

        private double GetGradePointFromPercentage(double percentage, GradeMaster? gradeMaster)
        {
            if (gradeMaster?.Thresholds != null && gradeMaster.Thresholds.Any())
            {
                var threshold = gradeMaster.Thresholds
                    .OrderByDescending(t => t.MinPercentage)
                    .FirstOrDefault(t => (decimal)percentage >= t.MinPercentage && (decimal)percentage <= t.MaxPercentage);
                
                if (threshold != null) return threshold.GradePoint;
            }

            // Fallback to standard 10-point scale
            if (percentage >= 80) return 10;
            if (percentage >= 75) return 9;
            if (percentage >= 70) return 8;
            if (percentage >= 60) return 7;
            if (percentage >= 50) return 6;
            if (percentage >= 45) return 5;
            if (percentage >= 40) return 4;
            return 0;
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

            int failedCount = marksMaster.StudentMarks!.Count(sm => (sm.Marks ?? 0) < GetPassingMarks(sm));
            overallResult.KtTheory = failedCount.ToString();
            overallResult.SGPI = marksMaster.SGPI;
            overallResult.Credits = marksMaster.StudentMarks
                .GroupBy(sm => sm.SubjectId)
                .Select(g => g.First().CreditMaster)
                .Where(cm => cm != null)
                .Sum(cm => double.TryParse(cm!.TotalCredits, out var val) ? val : 0)
                .ToString();

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
                
                decimal totalSGPI = allSems.Sum(r => r.SGPI ?? 0);
                marksMaster.CGPI = allSems.Count > 0 ? Math.Round(totalSGPI / allSems.Count, 2) : marksMaster.SGPI;
                overallResult.CGPI = marksMaster.CGPI;
            }
        }

        private double GetGradePoint(StudentMarks sm, GradeMaster? gm)
        {
            int marks = sm.Marks ?? 0;
            int outOf = GetOutOf(sm);
            double percentage = outOf > 0 ? (double)marks * 100 / outOf : 0;

            return GetGradePointFromPercentage(percentage, gm);
        }

        private async Task<bool> EvaluateRule(StudentMaster student, MarksMaster marksMaster, Rule rule)
        {
            foreach (var condition in rule.Conditions)
            {
                var provider = _registry.GetFactProvider(condition.FactName);
                if (provider == null) continue;

                double factValue = await provider.GetValueAsync(student, marksMaster);
                if (!CompareValues(factValue, condition.Operator, condition.Value))
                {
                    return false;
                }
            }
            return true;
        }

        private async Task ApplyRuleActions(MarksMaster marksMaster, Rule rule)
        {
            foreach (var action in rule.Actions)
            {
                var handler = _registry.GetActionHandler(action.ActionType);
                if (handler != null)
                {
                    await handler.ExecuteAsync(marksMaster, action, rule.OrdinanceSymbol);
                }
            }
        }

        private bool CompareValues(double factValue, string op, string targetValueStr)
        {
            if (!double.TryParse(targetValueStr, out double targetValue)) return false;
            return op switch
            {
                "Equals" => factValue == targetValue,
                "GreaterThan" => factValue > targetValue,
                "LessThan" => factValue < targetValue,
                "GreaterOrEqual" or "GreaterThanOrEqual" => factValue >= targetValue,
                "LessOrEqual" or "LessThanOrEqual" => factValue <= targetValue,
                "NotEquals" => factValue != targetValue,
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
                    .Where(mm => mm.ExamId == request.ExamId && mm.Pattern == request.Pattern && mm.SemesterId == request.SemId && !mm.IsDeleted);

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
             return 40; 
        }

        private int GetOutOf(StudentMarks sm)
        {
            var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
            return int.TryParse(credit?.HeadOutOf, out int o) ? o : 100;
        }

        private static int GetOutOfStatic(StudentMarks sm)
        {
            var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
            return int.TryParse(credit?.HeadOutOf, out int o) ? o : 100;
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
                    sm.Remark = "Successful";
                }
            }
        }

    }

}
