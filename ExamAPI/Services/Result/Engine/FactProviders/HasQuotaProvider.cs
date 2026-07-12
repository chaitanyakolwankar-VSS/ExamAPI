using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class HasQuotaProvider : IFactProvider
    {
        public string FactName => "HasQuota";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            return Task.FromResult(!string.IsNullOrEmpty(marksMaster.QuotaType) ? 1.0 : 0.0);
        }
    }
}
