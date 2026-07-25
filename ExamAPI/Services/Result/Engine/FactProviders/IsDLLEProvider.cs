using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class IsDLLEProvider : IFactProvider
    {
        public string FactName => "IsDLLE";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            return Task.FromResult(string.Equals(marksMaster.QuotaType, "DLLE", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0);
        }
    }
}
