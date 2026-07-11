using ExamAPI.Services.Report;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

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

        private bool TryGetCollegeId(out Guid collegeId)
        {
            return Guid.TryParse(User.FindFirstValue("CollegeId"), out collegeId);
        }

        [HttpPost("gazette")]
        public async Task<IActionResult> DownloadGazette([FromBody] ExamAPI.DTOs.GazetteRequestDto request)
        {
            if (!TryGetCollegeId(out var collegeId)) return Unauthorized();
            var pdfBytes = await _reportService.GenerateGazettePdfAsync(request, collegeId);
            return File(pdfBytes, "application/pdf", $"Gazette_{request.ExamId}.pdf");
        }

        [HttpPost("gazette/excel")]
        public async Task<IActionResult> DownloadGazetteExcel([FromBody] ExamAPI.DTOs.GazetteRequestDto request)
        {
            if (!TryGetCollegeId(out var collegeId)) return Unauthorized();
            var excelBytes = await _reportService.GenerateGazetteExcelAsync(request, collegeId);
            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Gazette_{request.ExamId}.xlsx");
        }

        [HttpGet("marksheet")]
        public async Task<IActionResult> DownloadMarksheet(Guid studId, Guid examId, string semId, string pattern, bool includeHistory = false, DateTime? resultDate = null, bool noRleForFail = false)
        {
            if (!TryGetCollegeId(out var collegeId)) return Unauthorized();
            var pdfBytes = await _reportService.GenerateMarksheetPdfAsync(studId, examId, semId, pattern, includeHistory, resultDate, collegeId, noRleForFail);
            return File(pdfBytes, "application/pdf", $"Marksheet_{studId}.pdf");
        }

        [HttpGet("bulk-marksheet")]
        public async Task<IActionResult> DownloadBulkMarksheet(Guid examId, string semId, string pattern, string generationType = "all", bool includeHistory = false, DateTime? resultDate = null, bool noRleForFail = false)
        {
            if (!TryGetCollegeId(out var collegeId)) return Unauthorized();
            var pdfBytes = await _reportService.GenerateBulkMarksheetPdfAsync(examId, semId, pattern, generationType, includeHistory, resultDate, collegeId, noRleForFail);
            return File(pdfBytes, "application/pdf", $"BulkMarksheets_{examId}.pdf");
        }
    }
}
