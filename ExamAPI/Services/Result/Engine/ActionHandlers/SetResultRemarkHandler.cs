using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.ActionHandlers
{
    public class SetResultRemarkHandler : IActionHandler
    {
        public string ActionType => "SetResultRemark";

        public Task ExecuteAsync(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
            marksMaster.ResultRemark = action.Target; // e.g., "RLE"
            return Task.CompletedTask;
        }
    }
}
