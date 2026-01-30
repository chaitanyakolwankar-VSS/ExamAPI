using ExamAPI.DTOs;

namespace ExamAPI.Services.Exam
{
    public interface IExamService
    {
        Task<ApiResponseDto<object>> CreateExamAsync(SaveExam dto);
        Task<List<GetExamResponse>> GetExam(GetExam dto);
        Task<ApiResponseDto<object>> UpdateExamAsync(UpdateExam dto);
        Task<ApiResponseDto<object>> DeleteExamAsync(DeleteExam dto);
    }
}
