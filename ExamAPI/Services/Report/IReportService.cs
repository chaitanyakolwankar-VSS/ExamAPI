using ExamAPI.DTOs;

namespace ExamAPI.Services.Report
{
    public interface IReportService
    {
        Task<byte[]> GenerateGazettePdfAsync(GazetteRequestDto request);
        Task<byte[]> GenerateGazetteExcelAsync(GazetteRequestDto request);
        Task<byte[]> GenerateMarksheetPdfAsync(Guid studId, Guid examId, Guid semId, string pattern, bool includeHistory = false);
        Task<byte[]> GenerateBulkMarksheetPdfAsync(Guid examId, Guid semId, string pattern, string generationType, bool includeHistory = false);
    }
}
