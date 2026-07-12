using ExamAPI.DTOs;

namespace ExamAPI.Services.Report
{
    public interface IReportService
    {
        Task<byte[]> GenerateGazettePdfAsync(GazetteRequestDto request, Guid collegeId);
        Task<byte[]> GenerateGazetteExcelAsync(GazetteRequestDto request, Guid collegeId);
        Task<byte[]> GenerateMarksheetPdfAsync(Guid studId, Guid examId, string semId, string pattern, bool includeHistory, DateTime? resultDate, Guid collegeId, bool noRleForFail = false);
        Task<byte[]> GenerateBulkMarksheetPdfAsync(Guid examId, string semId, string pattern, string generationType, bool includeHistory, DateTime? resultDate, Guid collegeId, bool noRleForFail = false);
    }
}
