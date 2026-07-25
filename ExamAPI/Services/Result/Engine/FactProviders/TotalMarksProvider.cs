using System;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class TotalMarksProvider : IFactProvider
    {
        public string FactName => "TotalMarks";

        public Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            if (marksMaster.StudentMarks == null) return Task.FromResult(0.0);

            var total = marksMaster.StudentMarks.Sum(sm => (double)(sm.Marks ?? 0));
            return Task.FromResult(total);
        }
    }
}
