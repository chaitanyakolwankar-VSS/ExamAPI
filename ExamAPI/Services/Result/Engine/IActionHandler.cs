using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine
{
    public interface IActionHandler
    {
        string ActionType { get; }
        Task ExecuteAsync(MarksMaster marksMaster, RuleAction action, string? symbol);
    }
}
