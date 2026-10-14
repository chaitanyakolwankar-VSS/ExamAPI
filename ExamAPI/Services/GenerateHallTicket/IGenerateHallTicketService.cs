using ExamAPI.DTOs;

namespace ExamAPI.Services.GenerateHallTicket
{
    public interface IGenerateHallTicketService
    {
        Task<List<HallTicketSubjects>> GetHallTicketSubject(HallTicketSubjectsRequest dto);
        Task<ApiResponseDto<object>> SaveTimeTable(SaveTimeTable dto);
        Task<List<StudentHallTicketData>> HallTickectStudentData(StudentHallTicketDataRequest dto);
        Task<HallTicketCollege> HallTicketCollegeData( );
        /// <summary>Time tables of every branch's copy of the exam, one block per branch + pattern + semester; null when the exam is not found.</summary>
        Task<TimeTableAllDto?> GetTimeTableAll(Guid examId);
    }
}
