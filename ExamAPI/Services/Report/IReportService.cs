using ExamAPI.DTOs;

namespace ExamAPI.Services.Report
{
    public interface IReportService
    {
        Task<byte[]> GenerateGazettePdfAsync(GazetteRequestDto request, Guid collegeId);
        Task<byte[]> GenerateGazetteExcelAsync(GazetteRequestDto request, Guid collegeId);
        Task<byte[]> GenerateMarksheetPdfAsync(Guid studId, Guid examId, string semId, string pattern, bool includeHistory, DateTime? resultDate, Guid collegeId, bool noRleForFail = false);

        /// <summary>
        /// The same marksheet the PDF prints, as data for the on-screen preview. Both come from one
        /// builder, so the preview cannot drift from the generated file.
        /// </summary>
        Task<MarksheetReportDto> GetMarksheetDataAsync(Guid studId, Guid examId, string semId, string pattern, bool includeHistory, DateTime? resultDate, Guid collegeId, bool noRleForFail = false);

        /// <summary>College branding (letterhead, signatures) used by the marksheet preview.</summary>
        Task<MarksheetBrandingDto> GetMarksheetBrandingAsync(Guid collegeId);
        Task<byte[]> GenerateBulkMarksheetPdfAsync(Guid examId, string semId, string pattern, Guid courseId, Guid ayid, string generationType, bool includeHistory, DateTime? resultDate, Guid collegeId, bool noRleForFail = false);
    }
}
