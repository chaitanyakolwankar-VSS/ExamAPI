using ExamAPI.Services.ExamApplications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>Staff: fees and application dates of ATKT, Re-Exam and Revaluation exams (college from the token).</summary>
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ExamFeesController : ControllerBase
    {
        private readonly IExamFeeService _service;

        public ExamFeesController(IExamFeeService service) => _service = service;

        [HttpGet("exams")]
        public Task<IActionResult> Exams([FromQuery] Guid courseId, [FromQuery] Guid ayid) =>
            Run(() => _service.GetExamsAsync(courseId, ayid));

        [HttpGet("{examId:guid}")]
        public Task<IActionResult> Get(Guid examId) => Run(() => _service.GetAsync(examId));

        [HttpPut("{examId:guid}")]
        public Task<IActionResult> Save(Guid examId, [FromBody] ExamFeeInput input) => Run(() => _service.SaveAsync(examId, input));

        [HttpDelete("{examId:guid}")]
        public Task<IActionResult> Delete(Guid examId) =>
            Run(async () => { await _service.DeleteAsync(examId); return new { removed = true }; });

        [HttpPost("{examId:guid}/preview")]
        public Task<IActionResult> Preview(Guid examId, [FromBody] ExamFeeInput input) => Run(() => _service.PreviewAsync(examId, input));

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
