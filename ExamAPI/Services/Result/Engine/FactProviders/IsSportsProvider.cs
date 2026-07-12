using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class IsSportsProvider : IFactProvider
    {
        public string FactName => "IsSports";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            return Task.FromResult(string.Equals(marksMaster.QuotaType, "SPORTS", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0);
        }
    }
}
