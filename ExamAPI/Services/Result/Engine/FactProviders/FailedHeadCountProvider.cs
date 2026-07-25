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
                .Count(sm => (sm.Marks ?? 0) < GetPassingMarks(sm));
                
            return Task.FromResult((double)count);
        }

        private int GetPassingMarks(StudentMarks sm)
        {
             var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
             if (credit != null && int.TryParse(credit.HeadPass, out int pass)) return pass;
             return 40; 
        }
    }
}
