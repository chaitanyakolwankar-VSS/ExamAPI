using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
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

        public ResultService(ApplicationDbContext context)
        {
            _context = context;
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
                var studentsQuery = _context.StudentMasters
                    .Where(s => s.CourseId == request.BranchId && !s.IsDeleted);

                if (request.IsSingleStudent && !string.IsNullOrEmpty(request.StudentId))
                {
                    studentsQuery = studentsQuery.Where(s => s.StudentId == request.StudentId);
                }

                var students = await studentsQuery.ToListAsync();

                if (!students.Any())
                {
                    return new ApiResponseDto<object> { Success = false, Message = "No students found for the selected criteria." };
                }

                var ruleSet = await _context.RuleSets
                    .Include(rs => rs.Rules.OrderBy(r => r.Priority))
                        .ThenInclude(r => r.Conditions)
                    .Include(rs => rs.Rules.OrderBy(r => r.Priority))
                        .ThenInclude(r => r.Actions)
                    .FirstOrDefaultAsync(rs => rs.PatternMaster.PatternName == request.Pattern && rs.IsActive && !rs.IsDeleted);

                if (ruleSet == null)
                {
                    return new ApiResponseDto<object> { Success = false, Message = "No active rule set found for the selected pattern." };
                }

                foreach (var student in students)
                {
                    await ProcessStudentResult(student, request, ruleSet);
                }

                await _context.SaveChangesAsync();

                return new ApiResponseDto<object> { Success = true, Message = "Results processed successfully." };
            }
            catch (Exception ex)
            {
                return new ApiResponseDto<object> { Success = false, Message = $"Error processing results: {ex.Message}" };
            }
        }

        private async Task ProcessStudentResult(StudentMaster student, ProcessResultRequest request, RuleSet ruleSet)
        {
            var marksMaster = await _context.MarksMasters
                .Include(mm => mm.StudentMarks)
                    .ThenInclude(sm => sm.Subject)
                .Include(mm => mm.StudentMarks)
                    .ThenInclude(sm => sm.CreditMaster)
                        .ThenInclude(cm => cm.Credits)
                .FirstOrDefaultAsync(mm => mm.StdMstId == student.Id && mm.ExamId == request.ExamId && !mm.IsDeleted);

            if (marksMaster == null || marksMaster.StudentMarks == null) return;

            foreach (var rule in ruleSet.Rules.Where(r => r.IsEnabled))
            {
                if (EvaluateRule(student, marksMaster, rule))
                {
                    ApplyRuleActions(marksMaster, rule);
                    if (rule.StopOnSuccess) break;
                }
            }

            CalculateFinalStatus(marksMaster);
        }

        private bool EvaluateRule(StudentMaster student, MarksMaster marksMaster, Rule rule)
        {
            foreach (var condition in rule.Conditions)
            {
                double factValue = GetFactValue(student, marksMaster, condition.FactName);
                if (!CompareValues(factValue, condition.Operator, condition.Value))
                {
                    return false;
                }
            }
            return true;
        }

        private double GetFactValue(StudentMaster student, MarksMaster marksMaster, string factName)
        {
            switch (factName)
            {
                case "FailedSubjectCount":
                    return marksMaster.StudentMarks.Count(sm => sm.Marks < GetPassingMarks(sm));
                case "TotalMarks":
                    return (double)marksMaster.StudentMarks.Sum(sm => sm.Marks ?? 0);
                case "Percentage":
                    var total = marksMaster.StudentMarks.Sum(sm => sm.Marks ?? 0);
                    var outOf = marksMaster.StudentMarks.Sum(sm => GetOutOf(sm));
                    return outOf > 0 ? (double)(total * 100 / outOf) : 0;
                default:
                    return 0;
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
                "GreaterOrEqual" => factValue >= targetValue,
                "LessOrEqual" => factValue <= targetValue,
                "NotEquals" => factValue != targetValue,
                _ => false
            };
        }

        private void ApplyRuleActions(MarksMaster marksMaster, Rule rule)
        {
            foreach (var action in rule.Actions)
            {
                switch (action.ActionType)
                {
                    case "AddGrace":
                        ApplyAddGrace(marksMaster, action, rule.OrdinanceSymbol);
                        break;
                    case "ApplyLookup":
                        ApplyLookupGrace(marksMaster, action, rule.OrdinanceSymbol);
                        break;
                }
            }
        }

        private void ApplyAddGrace(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
            var failedSubjects = marksMaster.StudentMarks
                .Where(sm => sm.Marks < GetPassingMarks(sm))
                .OrderBy(sm => GetPassingMarks(sm) - sm.Marks)
                .ToList();

            decimal totalGraceAvailable = action.MaxLimit ?? 0;

            foreach (var sm in failedSubjects)
            {
                if (totalGraceAvailable <= 0) break;

                int required = GetPassingMarks(sm) - (sm.Marks ?? 0);
                
                decimal limit1 = action.Param1Type == "% of Aggregate" 
                    ? (marksMaster.StudentMarks.Sum(x => GetOutOf(x)) * (action.Param1Value ?? 0) / 100)
                    : (action.Param1Value ?? 0);
                
                decimal limit2 = action.Param2Type == "% of Subject"
                    ? (GetOutOf(sm) * (action.Param2Value ?? 0) / 100)
                    : (action.Param2Value ?? decimal.MaxValue);

                decimal allowedForThisSubject = Math.Min(limit1, limit2);
                allowedForThisSubject = Math.Min(allowedForThisSubject, totalGraceAvailable);

                if (required <= allowedForThisSubject)
                {
                    sm.Marks += required;
                    sm.Grace = required.ToString() + symbol;
                    sm.Remark = "Passed by Ordinance";
                    totalGraceAvailable -= required;
                }
            }
        }

        private void ApplyLookupGrace(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
             var failedSubjects = marksMaster.StudentMarks
                .Where(sm => sm.Marks < GetPassingMarks(sm))
                .ToList();

             foreach(var sm in failedSubjects)
             {
                 int outOf = GetOutOf(sm);
                 var lookup = _context.GraceLookups
                    .Where(gl => gl.HeadMarksUpto >= outOf)
                    .OrderBy(gl => gl.HeadMarksUpto)
                    .FirstOrDefault();

                 if (lookup != null)
                 {
                     int required = GetPassingMarks(sm) - (sm.Marks ?? 0);
                     if (required <= lookup.GraceMarks)
                     {
                         sm.Marks += required;
                         sm.Grace = required.ToString() + symbol;
                         sm.Remark = "Passed by Resolution";
                     }
                 }
             }
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

        private void CalculateFinalStatus(MarksMaster marksMaster)
        {
            bool isFail = marksMaster.StudentMarks.Any(sm => sm.Marks < GetPassingMarks(sm));
            marksMaster.OverallRemark = isFail ? "Fail" : "Pass";
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
                    SubjectMarks = mm.StudentMarks?.ToDictionary(
                        sm => sm.Subject?.SubjectName ?? sm.Id.ToString(),
                        sm => $"{sm.Marks}/{GetOutOf(sm)}{(string.IsNullOrEmpty(sm.Grace) ? "" : " (" + sm.Grace + ")")}"
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
    }
}
