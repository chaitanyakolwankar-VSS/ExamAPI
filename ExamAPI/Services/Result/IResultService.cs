using ExamAPI.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ExamAPI.Services.Result
{
    public interface IResultService
    {
        Task<ApiResponseDto<object>> ProcessResultsAsync(ProcessResultRequest request, Guid collegeId);
        Task<ResultGridResponseDto> GetResultsAsync(ProcessResultRequest request, Guid collegeId);
        Task<byte[]> ExportResultsExcelAsync(ProcessResultRequest request, Guid collegeId);
        Task<byte[]> ExportResultsPdfAsync(ProcessResultRequest request, Guid collegeId);
    }
}
