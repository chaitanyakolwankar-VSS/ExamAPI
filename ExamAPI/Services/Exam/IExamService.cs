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
    }
}
