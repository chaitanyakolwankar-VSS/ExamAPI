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
            if (marksMaster.StudentMarks == null) return Task.CompletedTask;

            double downgradeVal = (double)(action.Param1Value ?? 1);
            double minThreshold = (double)(action.Param2Value ?? 4);

            var subjectGroups = marksMaster.StudentMarks.GroupBy(sm => sm.SubjectId).ToList();

            foreach (var group in subjectGroups)
            {
                bool appliesToGroup = true;

                if (action.Target == "Subject" && !group.Any(sm => sm.IsCarryForward))
                {
                    appliesToGroup = false;
                }

                if (appliesToGroup)
                {
                    foreach (var sm in group)
                    {
                        if (sm.GradePoint >= minThreshold)
                        {
                            sm.GradePoint = (int)Math.Max(minThreshold, sm.GradePoint.Value - downgradeVal);
                            sm.Grade = sm.GradePoint < minThreshold ? "F" : sm.Grade;
                        }
                    }
                }
            }

            return Task.CompletedTask;
        }
    }
}
