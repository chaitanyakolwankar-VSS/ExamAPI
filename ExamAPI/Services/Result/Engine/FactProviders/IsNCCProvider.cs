using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class IsNCCProvider : IFactProvider
    {
        public string FactName => "IsNCC";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            return Task.FromResult(string.Equals(marksMaster.QuotaType, "NCC", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0);
        }
    }
}
