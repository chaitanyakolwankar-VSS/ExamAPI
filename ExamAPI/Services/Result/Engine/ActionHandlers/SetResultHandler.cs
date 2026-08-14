using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.ActionHandlers
{
    public class SetResultHandler : IActionHandler
    {
        public string ActionType => "SetResult";

        public Task ExecuteAsync(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
            marksMaster.OverallRemark = OverallRemarks.Normalize(action.Target);
            return Task.CompletedTask;
        }
    }
}
