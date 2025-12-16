using ExamAPI.Data;
using ExamAPI.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Common
{
    public class AcademicYearService : IAcademicYearService
    {
        private readonly ApplicationDbContext _context;

        public AcademicYearService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<AcademicYearDto>> GetAllYearsAsync()
        {
            var dbYears = await _context.AcademicYears
                .Select(ay => new AcademicYearDto
                {
                    AYID = ay.AYID,
                    ShortDuration = ay.ShortDuration ?? "",
                    IsCurrent = ay.IsCurrent
                })
                .ToListAsync();
            return dbYears;
        }
    }
}
