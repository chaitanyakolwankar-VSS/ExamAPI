using ExamAPI.DTOs;

namespace ExamAPI.Services.ReleaseHallTicket
{
    public interface IReleaseHallticketService
    {
        Task<List<DeclareHallTicketDTO>> GetExam(GetDeclareExam dto);
        Task<List<DeclareHallTicketDTO>> GetTableExam(DeclareExamTable dto);
        Task<bool> ToggleReleaseHallTicket(ToggleReleaseHallTicketDTO dto);
    }
}
