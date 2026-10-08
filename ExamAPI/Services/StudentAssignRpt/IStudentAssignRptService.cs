using ExamAPI.DTOs;

namespace ExamAPI.Services.StudentAssignRpt
{
    public interface IStudentAssignRptService
    {

        Task<List<StudentAssignRptResDto>> GetReportAsync(StudentAssignRptDto request);

        Task<List<StudentCreditRptResponseDto>> GetCreditReportAsync(StudentAssignRptDto request);
    }
}
