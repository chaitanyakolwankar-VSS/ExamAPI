using ExamAPI.DTOs;

namespace ExamAPI.Services.Ordinance
{
    public interface IOrdinanceService
    {
        Task<IEnumerable<PatternDto>> GetPatternsAsync();
        Task<PatternDto> GetPatternByIdAsync(Guid patternId);
        Task<PatternDto> CreatePatternAsync(PatternCreateDto patternDto, Guid collegeId);
        Task<bool> UpdatePatternAsync(PatternUpdateDto patternDto);
        Task<bool> DeletePatternAsync(Guid patternId);
    }
}