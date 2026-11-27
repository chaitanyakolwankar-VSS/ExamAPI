using ExamAPI.Services.ExamApplications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>Staff: who applied and paid for an ATKT, Re-Exam or Revaluation exam, office payments, fixes, the fee report.</summary>
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ExamApplicationsController : ControllerBase
    {
        private readonly IExamApplicationAdminService _service;

        public ExamApplicationsController(IExamApplicationAdminService service) => _service = service;

        public class TextRequest
        {
            public string? Text { get; set; }
        }

        [HttpGet("exam/{examId:guid}")]
        public Task<IActionResult> List(Guid examId) => Run(() => _service.ListAsync(examId));

        [HttpPost("{applicationId:guid}/office")]
        public Task<IActionResult> Office(Guid applicationId, [FromBody] TextRequest request) => Run(() => _service.PayAtOfficeAsync(applicationId, request.Text ?? ""));

        [HttpPost("{applicationId:guid}/assign")]
        public Task<IActionResult> Assign(Guid applicationId) => Run(() => _service.RetryAssignAsync(applicationId));

        [HttpPost("{applicationId:guid}/cancel")]
        public Task<IActionResult> Cancel(Guid applicationId, [FromBody] TextRequest request) => Run(() => _service.CancelAsync(applicationId, request.Text ?? ""));

        [HttpPost("{applicationId:guid}/refund-done")]
        public Task<IActionResult> RefundDone(Guid applicationId, [FromBody] TextRequest request) => Run(() => _service.RefundDoneAsync(applicationId, request.Text ?? ""));

        [HttpGet("{applicationId:guid}/receipt")]
        public async Task<IActionResult> Receipt(Guid applicationId)
        {
            try
            {
                var (pdf, name) = await _service.ReceiptAsync(applicationId);
                return File(pdf, "application/pdf", name);
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet("report")]
        public Task<IActionResult> Report([FromQuery] FeeReportFilter filter) => Run(() => _service.ReportAsync(filter));

        [HttpGet("report/excel")]
        public async Task<IActionResult> ReportExcel([FromQuery] FeeReportFilter filter)
        {
            try
            {
                var (content, name) = await _service.ReportExcelAsync(filter);
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
            }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message }); }
        }

        private async Task<IActionResult> Run<T>(Func<Task<T>> action)
        {
            try
            {
                return Ok(await action());
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
        }
    }
}
