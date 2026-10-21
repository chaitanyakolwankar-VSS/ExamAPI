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
        private readonly IStudentDetailsService _details;

        public StudentAccessController(IStudentSignInService service, IStudentDetailsService details)
        {
            _service = service;
            _details = details;
        }

        public class TokenRequest
        {
            public string? Token { get; set; }
        }

        public class CodeCheckRequest
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
        public Task<IActionResult> Verify([FromBody] CodeCheckRequest request) => Answer(() => _service.VerifyAsync(request.Token, request.Code));

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

        /// <summary>Saves the fields this link lets the student change (others are ignored).</summary>
        [Authorize(Policy = StudentAccessAuth.Policy)]
        [HttpPut("details")]
        public Task<IActionResult> SaveDetails([FromBody] StudentDetailsUpdate update) => Answer(() => _details.SaveAsync(User, update));

        /// <summary>kind: photo or signature. The page shrinks phone pictures first; the limit is 2 MB.</summary>
        [Authorize(Policy = StudentAccessAuth.Policy)]
        [RequestSizeLimit(5 * 1024 * 1024)]
        [HttpPut("{kind:regex(^(photo|signature)$)}")]
        public Task<IActionResult> SaveImage(string kind, [FromBody] StudentImageUpload upload) => Answer(() => _details.SaveImageAsync(User, kind, upload.Image));

        /// <summary>The student's own photo or signature (only theirs: the session names the student).</summary>
        [Authorize(Policy = StudentAccessAuth.Policy)]
        [HttpGet("{kind:regex(^(photo|signature)$)}")]
        public async Task<IActionResult> Image(string kind)
        {
            try
            {
                var image = await _details.ReadImageAsync(User, kind);
                if (image == null) return NotFound();
                Response.Headers.CacheControl = "no-store";
                return File(image.Value.Bytes, image.Value.ContentType);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Your session has ended. Open the link from your email again." });
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
                return Unauthorized(new { message = "Your session has ended. Open the link from your email again." });
            }
        }
    }
}
