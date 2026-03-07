using ExamAPI.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Services.Fees
{
    public interface IFeesService
    {
        Task<List<BranchOptionDto>> GetCoursesAsync();
        Task<List<ExamOptionDto>> GetExamsAsync([FromQuery] Guid ayid, [FromQuery] Guid? courseId);
        Task<List<CategoryOptionDto>> GetCategoriesAsync();
        Task<List<FeesRecordDto>> GetFeesByExamAsync(GetFeesDto dto);

        Task<ApiResponseDto<object>> SaveFeesAsync(SaveFees dto);
        Task<ApiResponseDto<object>> DeleteFeesAsync(DeleteFeesDto dto);
    }
}