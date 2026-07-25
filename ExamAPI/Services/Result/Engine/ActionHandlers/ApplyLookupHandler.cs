using System;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Models;
using ExamAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Result.Engine.ActionHandlers
{
    public class ApplyLookupHandler : IActionHandler
    {
        public string ActionType => "ApplyLookup";
        private readonly ApplicationDbContext _context;

        public ApplyLookupHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task ExecuteAsync(MarksMaster marksMaster, RuleAction action, string? symbol)
        {
            // ApplyLookup currently maps to Resolution Grace (O.5042)
            await ApplyResolutionGraceAsync(marksMaster, symbol);
        }

        private async Task ApplyResolutionGraceAsync(MarksMaster marksMaster, string? symbol = "^")
        {
            if (marksMaster.StudentMarks == null || !marksMaster.ExamId.HasValue) return;

            var creditIds = marksMaster.StudentMarks
                .Where(sm => sm.CreditsId.HasValue)
                .Select(sm => sm.CreditsId!.Value)
                .Distinct()
                .ToList();

            var resolutionQuery = _context.Resolution
                .Where(r => r.ExamID == marksMaster.ExamId && !r.IsDeleted && r.CreditID.HasValue && creditIds.Contains(r.CreditID.Value));

            if (marksMaster.AcademicYearAYID.HasValue)
            {
                resolutionQuery = resolutionQuery.Where(r => r.AYID == marksMaster.AcademicYearAYID);
            }

            var resolutions = await resolutionQuery.ToListAsync();

            var failedSubjects = marksMaster.StudentMarks
                .Where(sm => sm.RawMarks.HasValue && string.IsNullOrEmpty(sm.Grace) && sm.RawMarks < GetPassingMarks(sm))
                .OrderBy(sm => GetPassingMarks(sm) - sm.RawMarks)
                .ToList();

            foreach (var sm in failedSubjects)
            {
                var resolution = resolutions.FirstOrDefault(r =>
                    r.CreditID == sm.CreditsId &&
                    string.Equals(r.Head, sm.Head, StringComparison.OrdinalIgnoreCase));

                if (resolution == null || !int.TryParse(resolution.Resolution, out var resolutionLimit) || resolutionLimit <= 0)
                {
                    continue;
                }

                int required = GetPassingMarks(sm) - (sm.RawMarks ?? 0);
                if (required <= resolutionLimit)
                {
                    sm.Resolution = required;
                    sm.Marks = (sm.RawMarks ?? 0) + required;
                    sm.Remark = "Successful";
                }
            }
        }

        private int GetPassingMarks(StudentMarks sm)
        {
             var credit = sm.CreditMaster?.Credits?.FirstOrDefault(c => c.Head == sm.Head);
             if (credit != null && int.TryParse(credit.HeadPass, out int pass)) return pass;
             return 40; 
        }
    }
}
