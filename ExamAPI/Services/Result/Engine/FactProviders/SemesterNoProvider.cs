using System;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class SemesterNoProvider : IFactProvider
    {
        public string FactName => "SemesterNo";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            double sNo = double.TryParse(marksMaster.SemesterId?.Replace("Sem-", ""), out var s) ? s : 0;
            return Task.FromResult(sNo);
        }
    }
}
