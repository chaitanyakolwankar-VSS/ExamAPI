using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class IsEXTProvider : IFactProvider
    {
        public string FactName => "IsEXT";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            return Task.FromResult(string.Equals(marksMaster.QuotaType, "EXT", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0);
        }
    }
}
