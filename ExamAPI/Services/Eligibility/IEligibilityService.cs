using Azure;
using Azure.Core;
using ExamAPI.DTOs;

namespace ExamAPI.Services.Eligibility
{
    public interface IEligibilityService
    {
        Task<List<EligibilityStudents>> EligibilityStudents(GetEligibilityStudents Request);
        Task<ApiResponseDto<object>> SaveEligibility(List<EligibilityStudents> Dto);
    }
}
