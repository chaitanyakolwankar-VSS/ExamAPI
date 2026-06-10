using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.ActionHandlers
{
    public class DowngradeGPHandler : IActionHandler
    {
        public string ActionType => "DowngradeGP";

        public Task ExecuteAsync(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
            // Note: In ResultService.cs, DowngradeGP is currently evaluated inside CalculateFinalStatus.
            // For now, we keep this as a no-op handler to satisfy the registry, 
            // but in the next phase, we might move the SGPI logic here as well.
            return Task.CompletedTask;
        }
    }
}
