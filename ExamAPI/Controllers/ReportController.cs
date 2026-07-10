using ExamAPI.Services.Report;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ReportController : ControllerBase
    {
        private readonly IReportService _reportService;

        public ReportController(IReportService reportService)
        {
            _reportService = reportService;
        }

        [HttpPost("gazette")]
        public async Task<IActionResult> DownloadGazette([FromBody] ExamAPI.DTOs.GazetteRequestDto request)
        {
            var pdfBytes = await _reportService.GenerateGazettePdfAsync(request);
            return File(pdfBytes, "application/pdf", $"Gazette_{request.ExamId}.pdf");
        }

        [HttpPost("gazette/excel")]
        public async Task<IActionResult> DownloadGazetteExcel([FromBody] ExamAPI.DTOs.GazetteRequestDto request)
        {
            var excelBytes = await _reportService.GenerateGazetteExcelAsync(request);
            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Gazette_{request.ExamId}.xlsx");
        }

        [HttpGet("marksheet")]
        public async Task<IActionResult> DownloadMarksheet(Guid studId, Guid examId, string semId, string pattern, bool includeHistory = false)
        {
            var pdfBytes = await _reportService.GenerateMarksheetPdfAsync(studId, examId, semId, pattern, includeHistory);
            return File(pdfBytes, "application/pdf", $"Marksheet_{studId}.pdf");
        }

        [HttpGet("bulk-marksheet")]
        public async Task<IActionResult> DownloadBulkMarksheet(Guid examId, string semId, string pattern, string generationType = "all", bool includeHistory = false)
        {
            var pdfBytes = await _reportService.GenerateBulkMarksheetPdfAsync(examId, semId, pattern, generationType, includeHistory);
            return File(pdfBytes, "application/pdf", $"BulkMarksheets_{examId}.pdf");
        }
    }
}
