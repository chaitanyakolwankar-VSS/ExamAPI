using ExamAPI.Services.Auth;
using ExamAPI.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// Platform admin only: the gateway accounts students pay into -- the platform default (used by every college
    /// without its own) and each college's own. Keys go in, never come out.
    /// </summary>
    [Route("api/Platform/payment")]
    [ApiController]
    [Authorize(Policy = AccessPolicies.PlatformAdmin)]
    public class PlatformPaymentController : ControllerBase
    {
        private readonly IPaymentAccountService _service;

        public PlatformPaymentController(IPaymentAccountService service) => _service = service;

        [HttpGet("default")]
        public Task<IActionResult> GetDefault() => Run(() => _service.GetAsync(null));

        [HttpPut("default")]
        public Task<IActionResult> SaveDefault([FromBody] PaymentAccountInput input) => Run(() => _service.SaveAsync(null, input));

        [HttpDelete("default")]
        public Task<IActionResult> DeleteDefault() => Run(async () => { await _service.DeleteAsync(null); return new { removed = true }; });

        [HttpGet("college/{collegeId:guid}")]
        public Task<IActionResult> Get(Guid collegeId) => Run(() => _service.GetAsync(collegeId));

        [HttpPut("college/{collegeId:guid}")]
        public Task<IActionResult> Save(Guid collegeId, [FromBody] PaymentAccountInput input) => Run(() => _service.SaveAsync(collegeId, input));

        [HttpDelete("college/{collegeId:guid}")]
        public Task<IActionResult> Delete(Guid collegeId) => Run(async () => { await _service.DeleteAsync(collegeId); return new { removed = true }; });

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
