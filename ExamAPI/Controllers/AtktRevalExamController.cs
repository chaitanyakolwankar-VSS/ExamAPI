using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.AtktRevalExam;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// ATKT (backlog) and Revaluation exam assignment -- the migration of the legacy
    /// frm_atktreval_exm_assign.aspx WebForm. College scope comes from the JWT via the
    /// DbContext global filter; nothing here reads a college id off the request.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class AtktRevalExamController : ControllerBase
    {
        private readonly IAtktRevalExamService _service;

        public AtktRevalExamController(IAtktRevalExamService service)
        {
            _service = service;
        }

        /// <summary>The configuration driving the screen: what may be selected and why.</summary>
        [HttpGet("policies")]
        public async Task<IActionResult> GetPolicies([FromQuery] string? mode)
            => Ok(await _service.GetPoliciesAsync(mode));

        /// <summary>Exams that can act as the source attempt (revaluation).</summary>
        [HttpGet("source-exams")]
        public async Task<IActionResult> GetSourceExams(
            [FromQuery] Guid courseId,
            [FromQuery] Guid ayid,
            [FromQuery] string semester,
            [FromQuery] string pattern,
            [FromQuery] string mode = AssignmentModes.Revaluation)
            => Ok(await _service.GetSourceExamsAsync(courseId, ayid, semester ?? string.Empty, pattern ?? string.Empty, mode));

        /// <summary>Exams students can be assigned into.</summary>
        [HttpGet("target-exams")]
        public async Task<IActionResult> GetTargetExams(
            [FromQuery] Guid courseId,
            [FromQuery] Guid ayid,
            [FromQuery] string semester,
            [FromQuery] string mode = AssignmentModes.Atkt,
            [FromQuery] Guid? sourceExamId = null)
            => Ok(await _service.GetTargetExamsAsync(courseId, ayid, semester ?? string.Empty, mode, sourceExamId));

        /// <summary>The student x subject matrix, with columns and rules supplied by the server.</summary>
        [HttpPost("matrix")]
        public async Task<IActionResult> GetMatrix([FromBody] AtktMatrixRequest request)
        {
            if (request == null) return BadRequest("Invalid request");
            return Ok(await _service.GetMatrixAsync(request));
        }

        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] AtktSaveRequest request)
        {
            if (request?.Filter == null || request.Students == null)
                return BadRequest("Invalid request");

            return Ok(await _service.SaveAsync(request));
        }

        [HttpPost("assign-all")]
        public async Task<IActionResult> AssignAll([FromBody] AtktAssignAllRequest request)
        {
            if (request?.Filter == null) return BadRequest("Invalid request");
            return Ok(await _service.AssignAllAsync(request));
        }

        [HttpDelete("assignment")]
        public async Task<IActionResult> DeleteAssignment([FromBody] AtktDeleteRequest request)
        {
            if (request?.Filter == null || request.StdMstId == Guid.Empty)
                return BadRequest("Invalid request");

            return Ok(await _service.DeleteAssignmentAsync(request));
        }

        [HttpPost("export")]
        public async Task<IActionResult> Export([FromBody] AtktExportRequest request)
        {
            if (request?.Filter == null) return BadRequest("Invalid request");

            var (content, fileName) = await _service.ExportAsync(request);
            return File(content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
    }
}
