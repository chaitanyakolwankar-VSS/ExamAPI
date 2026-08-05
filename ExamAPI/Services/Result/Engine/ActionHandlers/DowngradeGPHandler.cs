using System;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.ActionHandlers
{
    public class DowngradeGPHandler : IActionHandler
    {
        public string ActionType => "DowngradeGP";

        public Task ExecuteAsync(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
            if (marksMaster.StudentMarks == null || marksMaster.SubjectResults == null) return Task.CompletedTask;

            double downgradeVal = (double)(action.Param1Value ?? 1);
            double minThreshold = (double)(action.Param2Value ?? 4);

            foreach (var group in marksMaster.StudentMarks.GroupBy(sm => sm.SubjectId))
            {
                if (action.Target == "Subject" && !group.Any(sm => sm.IsCarryForward))
                {
                    continue;
                }

                var subjectResult = marksMaster.SubjectResults.FirstOrDefault(r => r.SubjectId == group.Key);
                if (subjectResult == null || subjectResult.GradePoint < minThreshold) continue;

                subjectResult.RawGradePoint = subjectResult.GradePoint; // Preserve original
                subjectResult.GradePoint = (int)Math.Max(minThreshold, subjectResult.GradePoint - downgradeVal);
                subjectResult.Grade = subjectResult.GradePoint < minThreshold ? "F" : subjectResult.Grade;
            }

            return Task.CompletedTask;
        }
    }
}
