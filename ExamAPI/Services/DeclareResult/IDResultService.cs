using ExamAPI.DTOs;

namespace ExamAPI.Services.DeclareResult
{
    public interface IDResultService
    {
        Task<List<DeclareResultDTO>> GetExam(GetDeclareExam dto);
        Task<List<DeclareResultDTO>> GetTableExam(DeclareExamTable dto);

        Task<bool> ToggleDeclare(ToggleDeclareResultDTO dto);
    }
}
