using ExamAPI.Services.Auth;
using ExamAPI.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// Platform admin only: the mail accounts emails are sent from -- the platform default (used by every college
    /// without its own) and each college's own. Passwords go in, never come out.
    /// </summary>
    [Route("api/Platform/email")]
    [ApiController]
    [Authorize(Policy = AccessPolicies.PlatformAdmin)]
    public class PlatformEmailController : ControllerBase
    {
        private readonly IEmailAccountService _service;

        public PlatformEmailController(IEmailAccountService service) => _service = service;

        public class TestRequest
        {
            public string? To { get; set; }
        }

        [HttpGet("default")]
        public Task<IActionResult> GetDefault() => Run(() => _service.GetAsync(null));

        [HttpPut("default")]
        public Task<IActionResult> SaveDefault([FromBody] EmailAccountInput input) => Run(() => _service.SaveAsync(null, input));

        [HttpDelete("default")]
        public Task<IActionResult> DeleteDefault() => Run(async () => { await _service.DeleteAsync(null); return new { removed = true }; });

        [HttpPost("default/test")]
        public Task<IActionResult> TestDefault([FromBody] TestRequest request) => Test(null, request);

        [HttpGet("colleges/{collegeId:guid}")]
        public Task<IActionResult> Get(Guid collegeId) => Run(() => _service.GetAsync(collegeId));

        [HttpPut("colleges/{collegeId:guid}")]
        public Task<IActionResult> Save(Guid collegeId, [FromBody] EmailAccountInput input) => Run(() => _service.SaveAsync(collegeId, input));

        [HttpDelete("colleges/{collegeId:guid}")]
        public Task<IActionResult> Delete(Guid collegeId) => Run(async () => { await _service.DeleteAsync(collegeId); return new { removed = true }; });

        [HttpPost("colleges/{collegeId:guid}/test")]
        public Task<IActionResult> TestCollege(Guid collegeId, [FromBody] TestRequest request) => Test(collegeId, request);

        private Task<IActionResult> Test(Guid? collegeId, TestRequest request) =>
            Run(async () =>
            {
                var error = await _service.SendTestAsync(collegeId, request.To ?? "");
                return new { sent = error == null, message = error ?? $"Test email sent to {request.To?.Trim()}." };
            });

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
        }
    }
}
