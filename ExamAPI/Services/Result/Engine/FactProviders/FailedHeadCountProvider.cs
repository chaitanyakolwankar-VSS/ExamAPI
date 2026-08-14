using System;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class FailedHeadCountProvider : IFactProvider
    {
        public string FactName => "FailedHeadCount";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            if (marksMaster.StudentMarks == null) return Task.FromResult(0.0);

            var count = marksMaster.StudentMarks
                .GroupBy(sm => sm.SubjectId)
                .Sum(group =>
                {
                    var verdict = SubjectPassEvaluator.Evaluate(group);

                    // A combined subject has no per-head verdict to count -- it fails as a whole.
                    return verdict.IsCombined
                        ? (verdict.IsPassed ? 0 : 1)
                        : group.Count(sm => !SubjectPassEvaluator.IsHeadPassed(sm));
                });

            return Task.FromResult((double)count);
        }
    }
}
