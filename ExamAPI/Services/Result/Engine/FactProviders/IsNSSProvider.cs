using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class IsNSSProvider : IFactProvider
    {
        public string FactName => "IsNSS";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            return Task.FromResult(marksMaster.QuotaType == "NSS" ? 1.0 : 0.0);
        }
    }
}
