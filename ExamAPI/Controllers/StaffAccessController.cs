using ExamAPI.Services.Auth;
using ExamAPI.Services.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// The staff site: open an emailed letters link, get and check a 6-digit code, then see and download your own
    /// letters with the staff session (<see cref="StaffAccessAuth"/>). Mirrors <see cref="StudentAccessController"/>:
    /// the first three need no sign-in (the token in the body is the key); the rest need the session, and neither an
    /// office JWT nor a student session works here. Nothing takes a staff id or an exam id from the browser.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class StaffAccessController : ControllerBase
    {
        private const string Ended = "Your session has ended. Open the link from your email again.";
        private readonly IStaffSignInService _service;
        private readonly IStaffPortalService _portal;

        public StaffAccessController(IStaffSignInService service, IStaffPortalService portal)
        {
            _service = service;
            _portal = portal;
        }

        public class StaffTokenRequest
        {
            public string? Token { get; set; }
        }

        public class StaffCodeCheckRequest
        {
            public string? Token { get; set; }
            public string? Code { get; set; }
        }

        [AllowAnonymous]
        [EnableRateLimiting(RateLimits.StudentAccess)]
        [HttpPost("open")]
        public async Task<IActionResult> Open([FromBody] StaffTokenRequest request) => Ok(await _service.OpenAsync(request.Token));

        [AllowAnonymous]
        [EnableRateLimiting(RateLimits.StudentAccess)]
        [HttpPost("code")]
        public Task<IActionResult> SendCode([FromBody] StaffTokenRequest request) => Answer(() => _service.SendCodeAsync(request.Token));

        [AllowAnonymous]
        [EnableRateLimiting(RateLimits.StudentAccess)]
        [HttpPost("verify")]
        public Task<IActionResult> Verify([FromBody] StaffCodeCheckRequest request) => Answer(() => _service.VerifyAsync(request.Token, request.Code));

        /// <summary>The person, the exam, the details to check and all their live letters.</summary>
        [Authorize(Policy = StaffAccessAuth.Policy)]
        [HttpGet("me")]
        public Task<IActionResult> Me() => Answer(() => _portal.HomeAsync(User));

        [Authorize(Policy = StaffAccessAuth.Policy)]
        [HttpPost("signout")]
        public async Task<IActionResult> SignOutStaff()
        {
            await _service.SignOutAsync(User);
            return Ok(new { message = "Signed out." });
        }

        /// <summary>Saves the person's own details to Staff Master; their letters print the new name from now on.</summary>
        [Authorize(Policy = StaffAccessAuth.Policy)]
        [HttpPost("details")]
        public Task<IActionResult> SaveDetails([FromBody] StaffDetailsView details) => Answer(() => _portal.SaveDetailsAsync(User, details));

        /// <summary>One of the person's own live letters as a PDF (anyone else's is not found).</summary>
        [Authorize(Policy = StaffAccessAuth.Policy)]
        [HttpGet("letters/{letterId:guid}/pdf")]
        public async Task<IActionResult> LetterPdf(Guid letterId)
        {
            try
            {
                var pdf = await _portal.LetterPdfAsync(User, letterId);
                Response.Headers.CacheControl = "no-store";
                return File(pdf.Bytes, "application/pdf", pdf.FileName);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = Ended });
            }
        }

        private async Task<IActionResult> Answer<T>(Func<Task<T>> action)
        {
            try
            {
                return Ok(await action());
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = Ended });
            }
        }
    }
}
