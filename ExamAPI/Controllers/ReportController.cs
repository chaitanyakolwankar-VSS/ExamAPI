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

        /// <summary>
        /// The marksheet as JSON for the staff preview card. Built by the same code as the PDF, so the
        /// preview shows exactly what the generated file prints.
        /// </summary>
        [HttpGet("marksheet/data")]
        public async Task<IActionResult> MarksheetData(Guid studId, Guid examId, string semId, string pattern, bool includeHistory = false, DateTime? resultDate = null, bool noRleForFail = false)
        {
            if (!TryGetCollegeId(out var collegeId)) return Unauthorized();
            try
            {
                var data = await _reportService.GetMarksheetDataAsync(studId, examId, semId, pattern, includeHistory, resultDate, collegeId, noRleForFail);
                return Ok(data);
            }
            catch (InvalidOperationException ex)
            {
                // The student has no processed result (or similar): say so on the preview instead of a 500.
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>College letterhead and signature images (stored paths) for the marksheet preview card.</summary>
        [HttpGet("marksheet/branding")]
        public async Task<IActionResult> MarksheetBranding()
        {
            if (!TryGetCollegeId(out var collegeId)) return Unauthorized();
            return Ok(await _reportService.GetMarksheetBrandingAsync(collegeId));
        }

        [HttpGet("bulk-marksheet")]
        public async Task<IActionResult> DownloadBulkMarksheet(Guid examId, string semId, string pattern,Guid courseId,Guid ayid, string generationType = "all", bool includeHistory = false, DateTime? resultDate = null, bool noRleForFail = false)
        {
            if (!TryGetCollegeId(out var collegeId)) return Unauthorized();
            var pdfBytes = await _reportService.GenerateBulkMarksheetPdfAsync(examId, semId, pattern,courseId,ayid, generationType, includeHistory, resultDate, collegeId, noRleForFail);
            return File(pdfBytes, "application/pdf", $"BulkMarksheets_{examId}.pdf");
        }
    }
}
