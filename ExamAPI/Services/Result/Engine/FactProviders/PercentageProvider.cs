using System;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class PercentageProvider : IFactProvider
    {
        public string FactName => "Percentage";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            if (marksMaster.StudentMarks == null || !marksMaster.StudentMarks.Any()) 
                return Task.FromResult(0.0);

            var total = marksMaster.StudentMarks.Sum(sm => sm.Marks ?? 0);
            var outOf = marksMaster.StudentMarks.Sum(sm => GetOutOf(sm));
            
            double percentage = outOf > 0 ? (double)(total * 100 / outOf) : 0;
            return Task.FromResult(percentage);
        }

        private int GetOutOf(StudentMarks sm)
        {
            var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
            return int.TryParse(credit?.HeadOutOf, out int o) ? o : 100;
        }
    }
}
