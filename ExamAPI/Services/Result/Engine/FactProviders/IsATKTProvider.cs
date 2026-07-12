using System;
using System.Threading.Tasks;
using ExamAPI.Models;
using ExamAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Result.Engine.FactProviders
{
    public class IsATKTProvider : IFactProvider
    {
        public string FactName => "IsATKT";
        private readonly ApplicationDbContext _context;

        public IsATKTProvider(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<double> GetValueAsync(StudentMaster? student, MarksMaster marksMaster)
        {
            var exam = await _context.Exams.FindAsync(marksMaster.ExamId);
            bool isAtkt = IsAtktExamType(exam?.ExamType);
            return isAtkt ? 1.0 : 0.0;
        }

        private bool IsAtktExamType(string? examType)
        {
            var key = NormalizeKey(examType);
            return key == "KT" || key == "ATKT" || key == "REEXAM";
        }

        private string NormalizeKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        }
    }
}
