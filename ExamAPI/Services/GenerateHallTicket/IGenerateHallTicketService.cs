using ExamAPI.DTOs;

namespace ExamAPI.Services.GenerateHallTicket
{
    public interface IGenerateHallTicketService
    {
        Task<List<HallTicketSubjects>> GetHallTicketSubject(HallTicketSubjectsRequest dto);
        Task<ApiResponseDto<object>> SaveTimeTable(SaveTimeTable dto);
        Task<List<StudentHallTicketData>> HallTickectStudentData(StudentHallTicketDataRequest dto);
        Task<HallTicketCollege> HallTicketCollegeData( );
        /// <summary>The college's hall ticket instructions for the settings screen (the defaults when it has not written its own).</summary>
        Task<HallTicketInstructionsDto> GetInstructionsAsync();
        /// <summary>Saves the on/off switch and the text; throws InvalidOperationException when the text breaks a limit or there is no college.</summary>
        Task<HallTicketInstructionsDto> SaveInstructionsAsync(HallTicketInstructionsSave dto);
        /// <summary>Time tables of every branch's copy of the exam, one block per branch + pattern + semester; null when the exam is not found.</summary>
        Task<TimeTableAllDto?> GetTimeTableAll(Guid examId);
    }
}
