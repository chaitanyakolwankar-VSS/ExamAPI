using System;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class FailedSubjectCountProvider : IFactProvider
    {
        public string FactName => "FailedSubjectCount";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            if (marksMaster.StudentMarks == null) return Task.FromResult(0.0);

            var count = marksMaster.StudentMarks
                .GroupBy(sm => sm.SubjectId)
                .Count(group => !SubjectPassEvaluator.Evaluate(group).IsPassed);
            return Task.FromResult((double)count);
        }
    }
}
