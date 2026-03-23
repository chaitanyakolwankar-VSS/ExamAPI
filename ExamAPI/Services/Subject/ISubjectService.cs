using ExamAPI.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Services.Subject
{
    public interface ISubjectService
    {
        Task<List<SubjectDtos>> GetSubjectsAsync(GetSubjectReqDtos dto);
        Task<ApiResponseDto<object>> CreateSubjectAsync(CreateSubjectDto dto);
        Task<ApiResponseDto<object>> SaveCreditAsync(SaveCreditsDto dto);
        Task<List<CreditDto>> GetSubjectCreditAsync(GetCredits dto);
        Task<ApiResponseDto<object>> UpdateCreditAsync(SaveCreditsDto dto);
        Task<ApiResponseDto<object>> DeleteCreditAsync(DeleteCreditDto dto);
        Task<ApiResponseDto<object>> DeleteSubjectAsync(DeleteSubjectDto dto);
        Task<ApiResponseDto<object>> GetPreviousCreditAsync(PreviousCredits dto);
        Task<ApiResponseDto<object>> SavePreviousCreditAsync(PreviousCredits dto);
        Task<ApiResponseDto<object>> CheckCreditAsync(GetCredits dto);
        Task<ApiResponseDto<object>> VerifyCreditAccess(VerifyCreditAccessDto dto);
    }
}
