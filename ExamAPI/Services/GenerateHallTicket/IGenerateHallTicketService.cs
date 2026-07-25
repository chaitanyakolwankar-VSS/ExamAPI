using ExamAPI.DTOs;

namespace ExamAPI.Services.GenerateHallTicket
{
    public interface IGenerateHallTicketService
    {
        Task<List<RegularExamResponse>> GetExam(GetExam dto);
        Task<List<HallTicketSubjects>> GetHallTicketSubject(HallTicketSubjectsRequest dto);
        Task<ApiResponseDto<object>> SaveTimeTable(SaveTimeTable dto);
        Task<List<StudentHallTicketData>> HallTickectStudentData(StudentHallTicketDataRequest dto);
        Task<HallTicketCollege> HallTicketCollegeData( );
    }
}
