using ExamAPI.Services.Auth;
using ExamAPI.Services.StudentAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// Student Master > Check emails: staff review missing / flagged emails before task links are sent.
    /// Signed-in staff only (the fallback policy); the college's email settings can be changed by its admin only.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class StudentEmailsController : ControllerBase
    {
        private readonly IStudentEmailReviewService _service;

        public StudentEmailsController(IStudentEmailReviewService service) => _service = service;

        /// <summary>Students of a year (optionally a branch and semester) with their email state, and the counts.</summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] StudentEmailFilter filter) => Ok(await _service.ListAsync(filter));

        /// <summary>Sets a student's email; with Verify also marks it checked by staff.</summary>
        [HttpPut("{studentId}")]
        public async Task<IActionResult> Update(string studentId, [FromBody] StudentEmailUpdate update)
        {
            try
            {
                return Ok(await _service.UpdateAsync(studentId, update));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        public class VerifyRequest
        {
            public List<string> StudentIds { get; set; } = new();
        }

        /// <summary>Marks the given students' emails as checked by staff (students without an email are skipped).</summary>
        [HttpPost("verify")]
        public async Task<IActionResult> Verify([FromBody] VerifyRequest request) =>
            Ok(new { verified = await _service.VerifyAsync(request.StudentIds) });

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings() => Ok(await _service.GetSettingsAsync());

        /// <summary>Allowed domains, exam office email and daily limit; re-checks unverified emails against the domains.</summary>
        [HttpPut("settings")]
        [Authorize(Policy = AccessPolicies.CollegeAdmin)]
        public async Task<IActionResult> SaveSettings([FromBody] EmailSettingsDto dto)
        {
            try
            {
                return Ok(new { flagged = await _service.SaveSettingsAsync(dto) });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
