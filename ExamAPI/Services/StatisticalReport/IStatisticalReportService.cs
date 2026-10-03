using ExamAPI.DTOs;

namespace ExamAPI.Services.StatisticalReport;

public interface IStatisticalReportService
{
    Task<ApiResponseDto<StatisticalReportDto>> GetReportAsync(StatisticalReportRequestDto request, Guid collegeId);
    Task<ApiResponseDto<byte[]>> GenerateExcelAsync(StatisticalReportRequestDto request, Guid collegeId);
}
