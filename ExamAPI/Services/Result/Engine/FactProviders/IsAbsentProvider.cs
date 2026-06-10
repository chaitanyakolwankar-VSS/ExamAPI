using System;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class IsAbsentProvider : IFactProvider
    {
        public string FactName => "IsAbsent";
        private const string ABSENT_REMARK = "Ab";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            if (marksMaster.StudentMarks == null) return Task.FromResult(0.0);

            var isAbsent = marksMaster.StudentMarks.Any(sm => 
                sm.Marks == null && string.Equals(sm.Remark, ABSENT_REMARK, StringComparison.OrdinalIgnoreCase));
            
            return Task.FromResult(isAbsent ? 1.0 : 0.0);
        }
    }
}
