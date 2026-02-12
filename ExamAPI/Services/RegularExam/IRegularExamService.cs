using ExamAPI.DTOs;

namespace ExamAPI.Services.RegularExam
{
    public interface IRegularExamService
    {
        Task<List<RegularExamResponse>> GetExam(GetExam dto);
        Task<ApiResponseDto<object>> CheckCredits(CheckCredits dto);

        Task<RegularStudentResponse> GetStudents(RegularExamStudents dto);
        Task<ApiResponseDto<object>> SaveRegularExamStudents(SaveRegularExamStudentsDto dto);
        Task<ApiResponseDto<object>> UpdateRegularExamStudents(SaveRegularExamStudentsDto dto);
    }
}
