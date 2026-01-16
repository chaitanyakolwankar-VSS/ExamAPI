using ExamAPI.DTOs;

namespace ExamAPI.Services.CollegeDetail
{
    public interface ICollegeDetailService
    {
        Task<Guid> CreateAsync(CreateCollegeDTO dto);
    }
}
