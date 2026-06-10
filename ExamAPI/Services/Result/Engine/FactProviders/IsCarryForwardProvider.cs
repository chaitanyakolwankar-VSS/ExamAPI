using System;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class IsCarryForwardProvider : IFactProvider
    {
        public string FactName => "IsCarryForward";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            if (marksMaster.StudentMarks == null) return Task.FromResult(0.0);

            var hasCarryForward = marksMaster.StudentMarks.Any(sm => sm.IsCarryForward);
            return Task.FromResult(hasCarryForward ? 1.0 : 0.0);
        }
    }
}
