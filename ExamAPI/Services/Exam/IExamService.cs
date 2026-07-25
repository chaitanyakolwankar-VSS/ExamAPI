using ExamAPI.DTOs;

namespace ExamAPI.Services.Exam
{
    public interface IExamService
    {
        Task<ApiResponseDto<object>> CreateExamAsync(Exams dto);
        Task<ApiResponseDto<object>> SearchExam(Exams dto);
        Task<List<GetExamResponse>> GetExam(GetExam dto);
        Task<ApiResponseDto<object>> UpdateExamAsync(UpdateExam dto);
        Task<ApiResponseDto<object>> DeleteExamAsync(DeleteExam dto);

        Task<List<ResolutionExamResponse>> GetResolutionExam(GetResolutionExam dto);
        Task<List<GetCreditHeadResolutionres>> GetCreditHeadResolution(GetCreditHeadResolutionReq dto);

        Task<ApiResponseDto<object>> SaveCreditHeadResolutionres(SaveCreditHeadResolutionres dto);
        Task<ApiResponseDto<object>> UpdateCreditHeadResolutionres(SaveCreditHeadResolutionres dto);
    }
}
