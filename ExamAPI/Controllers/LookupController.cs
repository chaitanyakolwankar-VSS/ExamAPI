using ExamAPI.DTOs;
using ExamAPI.Services.Lookup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// Repeated lookup data (academic years, courses, patterns, semesters, exam and subject
    /// drop-downs) from one place. Any signed-in college user; tenant scoping comes from the
    /// global query filter.
    /// </summary>
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class LookupController : ControllerBase
    {
        private readonly ILookupService _service;

        public LookupController(ILookupService service)
        {
            _service = service;
        }

        [HttpGet("bootstrap")]
        public async Task<IActionResult> Bootstrap(CancellationToken ct)
            => Ok(await _service.GetBootstrapAsync(ct));

        /// <summary>
        /// purpose: master | regular | all | seatNo | hallTicket | process. seatNo requires semester.
        /// </summary>
        [HttpGet("exams")]
        public async Task<IActionResult> Exams(
            [FromQuery] Guid courseId, [FromQuery] Guid ayid, [FromQuery] string? purpose,
            [FromQuery] string? semester, CancellationToken ct)
        {
            var key = ExamPurposes.Normalize(purpose);
            if (key == null)
            {
                return BadRequest(new ApiResponseDto<object>
                {
                    Success = false,
                    Message = $"Unknown purpose '{purpose}'. Expected one of: {string.Join(", ", ExamPurposes.Names)}.",
                });
            }

            if (ExamPurposes.RequiresSemester(key) && string.IsNullOrWhiteSpace(semester))
            {
                return BadRequest(new ApiResponseDto<object>
                {
                    Success = false,
                    Message = $"Purpose '{key}' requires the semester parameter.",
                });
            }

            return Ok(await _service.GetExamsAsync(courseId, ayid, key, semester, ct));
        }

        [HttpGet("subjects")]
        public async Task<IActionResult> Subjects(
            [FromQuery] Guid courseId, [FromQuery] string? pattern, [FromQuery] string? semester, CancellationToken ct)
            => Ok(await _service.GetSubjectsAsync(courseId, pattern, semester, ct));
    }
}
