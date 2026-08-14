using System;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class IsAbsentProvider : IFactProvider
    {
        public string FactName => "IsAbsent";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            if (marksMaster.StudentMarks == null) return Task.FromResult(0.0);

            var isAbsent = marksMaster.StudentMarks.Any(sm => sm.IsAbsent);

            return Task.FromResult(isAbsent ? 1.0 : 0.0);
        }
    }
}
