using ExamAPI.Services.Auth;
using ExamAPI.Services.StudentAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// The student site: open an emailed link, get and check a 6-digit code, then act on that one task with the
    /// student session (<see cref="StudentAccessAuth"/>). The first three need no sign-in; the token in the body is
    /// the key. The rest need the student session; a staff token does not work here.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class StudentAccessController : ControllerBase
    {
        private readonly IStudentSignInService _service;

        public StudentAccessController(IStudentSignInService service) => _service = service;

        public class TokenRequest
        {
            public string? Token { get; set; }
        }

        public class VerifyRequest
        {
            public string? Token { get; set; }
            public string? Code { get; set; }
        }

        [AllowAnonymous]
        [EnableRateLimiting(RateLimits.StudentAccess)]
        [HttpPost("open")]
        public async Task<IActionResult> Open([FromBody] TokenRequest request) => Ok(await _service.OpenAsync(request.Token));

        [AllowAnonymous]
        [EnableRateLimiting(RateLimits.StudentAccess)]
        [HttpPost("code")]
        public Task<IActionResult> SendCode([FromBody] TokenRequest request) => Answer(() => _service.SendCodeAsync(request.Token));

        [AllowAnonymous]
        [EnableRateLimiting(RateLimits.StudentAccess)]
        [HttpPost("verify")]
        public Task<IActionResult> Verify([FromBody] VerifyRequest request) => Answer(() => _service.VerifyAsync(request.Token, request.Code));

        [Authorize(Policy = StudentAccessAuth.Policy)]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            try
            {
                return Ok(await _service.HomeAsync(User));
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Your session has ended. Open the link from your email again." });
            }
        }

        [Authorize(Policy = StudentAccessAuth.Policy)]
        [HttpPost("signout")]
        public async Task<IActionResult> SignOutStudent()
        {
            await _service.SignOutAsync(User);
            return Ok(new { message = "Signed out." });
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
        }
    }
}
