using ExamAPI.DTOs;

namespace ExamAPI.Services.Common
{
    public interface IAcademicYearService
    {
        Task<List<AcademicYearDto>> GetAllYearsAsync();
    }
}