using ExamAPI.DTOs;

namespace ExamAPI.Services.CollegeDetail
{
    public interface ICollegeDetailService
    {
        Task<CollegeDetailDTO?> GetAsync();

        Task<Guid> CreateAsync(CreateCollegeDTO dto);

        Task<Guid> UpdateAsync(Guid id,CreateCollegeDTO dto);
    }
}
