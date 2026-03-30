using ExamAPI.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ExamAPI.Services.Result
{
    public interface IResultService
    {
        Task<IEnumerable<ExamOptionDto>> GetExamsAsync(Guid branchId, string semId, string pattern);
        Task<ApiResponseDto<object>> ProcessResultsAsync(ProcessResultRequest request, Guid collegeId);
        Task<ApiResponseDto<IEnumerable<ResultDataDto>>> GetResultsAsync(ProcessResultRequest request, Guid collegeId);
    }
}
