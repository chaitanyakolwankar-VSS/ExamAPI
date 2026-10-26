using ExamAPI.DTOs;

namespace ExamAPI.Services.AttendenceReport
{
    public interface IAttendenceReportService
    {
        Task<List<RegularExamResponse>> GetExam(ReportExamRequest dto);

        Task<List<AttendenceReportResponseDTO>> GetSubjectStudentCount(
            AttendenceReportRequestDTO request);

        Task<List<AttendenceSeatNoResponseDTO>> GetSubjectSeatNo(
      AttendenceSeatNoRequestDTO request);
    }
}