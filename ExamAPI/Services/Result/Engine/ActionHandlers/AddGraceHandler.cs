using System;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.ActionHandlers
{
    public class AddGraceHandler : IActionHandler
    {
        public string ActionType => "AddGrace";

        public Task ExecuteAsync(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
            if (marksMaster.StudentMarks == null) return Task.CompletedTask;

            var failedSubjects = marksMaster.StudentMarks
                .Where(sm => sm.Marks.HasValue && sm.Marks < GetPassingMarks(sm))
                .OrderBy(sm => GetPassingMarks(sm) - sm.Marks)
                .ToList();

            decimal totalGraceAvailable = action.MaxLimit ?? action.Param1Value ?? 0;

            foreach (var sm in failedSubjects)
            {
                if (totalGraceAvailable <= 0) break;

                int required = GetPassingMarks(sm) - (sm.Marks ?? 0);
                
                decimal limit1 = CalculateActionLimit(action.Param1Type, action.Param1Value, marksMaster, sm, false, action.Expression);
                decimal limit2 = CalculateActionLimit(action.Param2Type, action.Param2Value, marksMaster, sm, true, action.Expression);

                decimal allowedForThisSubject = Math.Min(limit1, limit2);
                allowedForThisSubject = Math.Min(allowedForThisSubject, totalGraceAvailable);

                if (required <= allowedForThisSubject)
                {
                    ApplyGraceToMark(sm, required, symbol, "Passed by Ordinance");
                    totalGraceAvailable -= required;
                }
            }

            return Task.CompletedTask;
        }

        private int GetPassingMarks(StudentMarks sm)
        {
             var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
             if (credit != null && int.TryParse(credit.HeadPass, out int pass)) return pass;
             return 40; 
        }

        private static void ApplyGraceToMark(StudentMarks sm, int required, string? symbol, string remark)
        {
            sm.Marks = (sm.RawMarks ?? 0) + (sm.Resolution ?? 0) + required;
            sm.Grace = required.ToString() + (symbol ?? string.Empty);
            sm.Remark = "Successful";
        }

        private decimal CalculateActionLimit(string? paramType, decimal? value, MarksMaster marksMaster, StudentMarks sm, bool noneMeansUnlimited, string? expressionStr = null)
        {
            var normalizedType = NormalizeKey(paramType);
            if (normalizedType == "NONE")
            {
                return noneMeansUnlimited ? decimal.MaxValue : 0;
            }

            if (!string.IsNullOrEmpty(expressionStr))
            {
                try
                {
                    var ncalcExpr = new NCalc.Expression(expressionStr);
                    ncalcExpr.Parameters["SubjectOutOf"] = (double)GetOutOf(sm);
                    ncalcExpr.Parameters["AggregateOutOf"] = (double)marksMaster.StudentMarks!.Sum(x => GetOutOf(x));
                    ncalcExpr.Parameters["ParamValue"] = (double)(value ?? 0);
                    
                    var result = ncalcExpr.Evaluate();
                    return Convert.ToDecimal(result);
                }
                catch
                {
                    // Fallback to standard logic if expression fails
                }
            }

            var rawValue = value ?? 0;
            return normalizedType switch
            {
                "PERCENTOFAGGREGATE" => (decimal)marksMaster.StudentMarks!.Sum(x => GetOutOf(x)) * rawValue / 100,
                "PERCENTOFSUBJECT" => (decimal)GetOutOf(sm) * rawValue / 100,
                _ => rawValue
            };
        }

        private int GetOutOf(StudentMarks sm)
        {
            var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
            return int.TryParse(credit?.HeadOutOf, out int o) ? o : 100;
        }

        private string NormalizeKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        }
    }
}
