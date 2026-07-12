using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using ExamAPI.Models;
using ExamAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class YearlyPercentageCreditsEarnedProvider : IFactProvider
    {
        public string FactName => "YearlyPercentageCreditsEarned";
        private readonly ApplicationDbContext _context;

        public YearlyPercentageCreditsEarnedProvider(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            if (string.IsNullOrEmpty(marksMaster.SemesterId)) return 0;
            
            int currentSem = int.TryParse(marksMaster.SemesterId.Replace("Sem-", ""), out var s) ? s : 0;
            if (currentSem % 2 != 0) return 100; // Only check at end of Sem 2, 4, 6, 8

            int prevSem = currentSem - 1;
            string prevSemId = $"Sem-{prevSem}";

            var yearlyResults = await _context.StudentsOverallResults
                .Where(r => r.StdMstId == marksMaster.StdMstId && (r.SemesterId == marksMaster.SemesterId || r.SemesterId == prevSemId))
                .ToListAsync();

            double totalCredits = yearlyResults.Sum(r => double.TryParse(r.Credits, out var c) ? c : 0);
            
            double earnedCurrent = marksMaster.StudentMarks!
                .GroupBy(sm => sm.SubjectId)
                .Where(g => g.All(sm => (sm.Marks ?? 0) >= GetPassingMarks(sm)))
                .Sum(g => double.TryParse(g.First().CreditMaster?.TotalCredits, out var c) ? c : 0);

            double earnedPrev = yearlyResults
                .Where(r => r.SemesterId == prevSemId)
                .Sum(r => {
                    return double.TryParse(r.Credits, out var c) && r.KtTheory == "0" ? c : 0;
                });

            return totalCredits > 0 ? ((earnedCurrent + earnedPrev) * 100 / totalCredits) : 0;
        }

        private int GetPassingMarks(StudentMarks sm)
        {
             var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
             if (credit != null && int.TryParse(credit.HeadPass, out int pass)) return pass;
             return 40; 
        }
    }
}
