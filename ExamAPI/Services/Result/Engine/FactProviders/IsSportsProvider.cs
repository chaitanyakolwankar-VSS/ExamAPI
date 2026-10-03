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
            // The seat-number screen stores the short code "SP"; "SPORTS" is the long form. Accept both.
            var quota = marksMaster.QuotaType;
            var isSports = string.Equals(quota, "SP", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(quota, "SPORTS", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(isSports ? 1.0 : 0.0);
        }
    }
}
