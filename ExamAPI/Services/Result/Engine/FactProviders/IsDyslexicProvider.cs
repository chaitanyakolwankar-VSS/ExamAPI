using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class IsDyslexicProvider : IFactProvider
    {
        public string FactName => "IsDyslexic";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            return Task.FromResult(marksMaster.QuotaType == "LD" ? 1.0 : 0.0);
        }
    }
}
