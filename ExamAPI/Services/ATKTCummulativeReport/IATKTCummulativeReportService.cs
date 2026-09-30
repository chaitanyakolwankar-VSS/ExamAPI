using ExamAPI.DTOs;

namespace ExamAPI.Services.ATKTCummulativeReport
{
    public interface IATKTCummulativeReportService
    {
        Task<List<RegularExamResponse>> GetExam(ReportExamRequest dto);
        Task<List<HeadTypeResponse>> GetHeadType(HeadType dto);
        Task<List<AtktReportResponse>> GetReportData(AtktReportRequest dto);
    }
}
