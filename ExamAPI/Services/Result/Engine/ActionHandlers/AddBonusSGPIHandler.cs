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
            // Note: In ResultService.cs, AddBonusSGPI is currently evaluated inside CalculateFinalStatus.
            return Task.CompletedTask;
        }
    }
}
