using ExamAPI.DTOs;

namespace ExamAPI.Services.AssignSeatNo
{
    public interface IAssignSeatNoService
    {
        Task<List<ExamResponse>> GetExam(GetAssignSeatNoExam dto);
        Task<List<AssignSeatNoStudents>> GetStudents(GetAssignSeatNoStudents dto);
        Task<ApiResponseDto<object>> UpdateSeatNo(SaveSeatNoRequest dto);
    }
}
