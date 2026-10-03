using ExamAPI.DTOs;
using ExamAPI.Services.Auth;
using ExamAPI.Services.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// Platform (developer) console: list colleges, provision a new one, and manage a college's
    /// admins, branches and academic years. Every action requires the PlatformAdmin policy; the
    /// attribute is on each action (not only the class) so a new action cannot silently be left open.
    /// Bad input and rule violations (e.g. a 3rd admin) come back as 400 { message }.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class PlatformController : ControllerBase
    {
        private readonly IProvisionCollegeService _provision;
        private readonly IPlatformCollegeService _colleges;

        public PlatformController(IProvisionCollegeService provision, IPlatformCollegeService colleges)
        {
            _provision = provision;
            _colleges = colleges;
        }

        [HttpGet("colleges")]
        [Authorize(Policy = AccessPolicies.PlatformAdmin)]
        public async Task<IActionResult> GetColleges(CancellationToken ct) =>
            Ok(await _colleges.ListAsync(ct));

        [HttpGet("colleges/{id:guid}")]
        [Authorize(Policy = AccessPolicies.PlatformAdmin)]
        public async Task<IActionResult> GetCollege(Guid id, CancellationToken ct)
        {
            var detail = await _colleges.GetAsync(id, ct);
            return detail == null ? NotFound(new { message = "College not found." }) : Ok(detail);
        }

        /// <summary>
        /// Multipart provision. Lists use indexed form keys: Branches[0].Name, Branches[0].Code,
        /// Patterns[0], Admins[0].Username ..., AcademicYear.FullDuration; files are Logo and Banner.
        /// Re-posting an existing CollegeCode returns that college and fills only what is missing.
        /// </summary>
        [HttpPost("colleges")]
        [Authorize(Policy = AccessPolicies.PlatformAdmin)]
        [Consumes("multipart/form-data")]
        public Task<IActionResult> ProvisionCollege([FromForm] ProvisionCollegeRequest request, CancellationToken ct) =>
            Run(async () => Ok(await _provision.ProvisionAsync(request, ct)));

        [HttpPost("colleges/{id:guid}/admins")]
        [Authorize(Policy = AccessPolicies.PlatformAdmin)]
        public Task<IActionResult> AddAdmin(Guid id, [FromBody] AdminInput input, CancellationToken ct) =>
            Run(async () => Ok(await _colleges.AddAdminAsync(id, input, ct)));

        [HttpDelete("colleges/{id:guid}/admins/{userId:guid}")]
        [Authorize(Policy = AccessPolicies.PlatformAdmin)]
        public Task<IActionResult> RemoveAdmin(Guid id, Guid userId, CancellationToken ct) =>
            Run(async () => await _colleges.RemoveAdminAsync(id, userId, ct)
                ? Ok(new { message = "Admin removed." })
                : NotFound(new { message = "Admin not found in this college." }));

        [HttpPost("colleges/{id:guid}/branches")]
        [Authorize(Policy = AccessPolicies.PlatformAdmin)]
        public Task<IActionResult> AddBranch(Guid id, [FromBody] BranchInput input, CancellationToken ct) =>
            Run(async () => Ok(await _colleges.AddBranchAsync(id, input, ct)));

        [HttpPost("colleges/{id:guid}/academic-years")]
        [Authorize(Policy = AccessPolicies.PlatformAdmin)]
        public Task<IActionResult> AddAcademicYear(Guid id, [FromBody] AddAcademicYearRequest input, CancellationToken ct) =>
            Run(async () => Ok(await _colleges.AddAcademicYearAsync(id, input, ct)));

        [HttpPut("colleges/{id:guid}/academic-years/{ayId:guid}/current")]
        [Authorize(Policy = AccessPolicies.PlatformAdmin)]
        public Task<IActionResult> SetCurrentAcademicYear(Guid id, Guid ayId, CancellationToken ct) =>
            Run(async () => Ok(await _colleges.SetCurrentAcademicYearAsync(id, ayId, ct)));

        // Validation / rule violations -> 400 with a readable message; unknown college -> 404.
        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
