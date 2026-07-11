using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.ActionHandlers
{
    public class AddBonusSGPIHandler : IActionHandler
    {
        public string ActionType => "AddBonusSGPI";

        public Task ExecuteAsync(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
            var bonus = (decimal)(action.Param1Value ?? 0);
            marksMaster.SGPI = (marksMaster.SGPI ?? 0) + bonus;

            if (action.MaxLimit.HasValue && marksMaster.SGPI > action.MaxLimit.Value)
            {
                marksMaster.SGPI = action.MaxLimit.Value;
            }

            if (!string.IsNullOrEmpty(symbol))
            {
                marksMaster.ResultRemark = string.IsNullOrEmpty(marksMaster.ResultRemark) 
                    ? symbol 
                    : $"{marksMaster.ResultRemark} {symbol}";
            }

            return Task.CompletedTask;
        }
    }
}
