using ExamAPI.DTOs;

namespace ExamAPI.Services.Report
{
    public interface IReportService
    {
        Task<byte[]> GenerateGazettePdfAsync(GazetteRequestDto request);
        Task<byte[]> GenerateGazetteExcelAsync(GazetteRequestDto request);
        Task<byte[]> GenerateMarksheetPdfAsync(Guid studId, Guid examId, string semId, string pattern, bool includeHistory = false);
        Task<byte[]> GenerateBulkMarksheetPdfAsync(Guid examId, string semId, string pattern, string generationType, bool includeHistory = false);
    }
}
