using ExamAPI.Services.StudentAccess;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// Student Master > Student links: staff send a class its links and follow them (opened, updated), stop one or
    /// all, move the deadline, remind and resend. Signed-in staff only (the fallback policy); scoped to the
    /// staff member's college by the tenant filter.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class StudentLinksController : ControllerBase
    {
        private readonly ITaskLinkService _service;
        private readonly StudentSiteUrl _site;

        public StudentLinksController(ITaskLinkService service, StudentSiteUrl site)
        {
            _service = service;
            _site = site;
        }

        public class DeadlineRequest
        {
            /// <summary>The last day the links work (local date).</summary>
            public DateTime Date { get; set; }
        }

        [HttpPost("send")]
        public Task<IActionResult> Send([FromBody] SendLinksRequest request) => Answer(async () =>
        {
            var baseUrl = _site.Resolve(request.AppUrl)
                ?? throw new InvalidOperationException("The student site address isn't known to the server. Set StudentAccess:AppUrl in the API settings.");
            return await _service.SendAsync(request, baseUrl);
        });

        [HttpGet("batches")]
        public async Task<IActionResult> Batches([FromQuery] LinkBatchFilter filter) => Ok(await _service.ListBatchesAsync(filter));

        [HttpGet("batches/{id:guid}")]
        public Task<IActionResult> Batch(Guid id) => Answer(() => _service.GetBatchAsync(id));

        [HttpPost("batches/{id:guid}/deadline")]
        public Task<IActionResult> Deadline(Guid id, [FromBody] DeadlineRequest request) => Answer(() => _service.SetDeadlineAsync(id, request.Date));

        [HttpPost("batches/{id:guid}/stop")]
        public Task<IActionResult> StopBatch(Guid id) => Answer(async () => new { stopped = await _service.StopBatchAsync(id) });

        /// <summary>who=notOpened (default): students who haven't opened their link; who=notUpdated: everyone who hasn't saved yet.</summary>
        [HttpPost("batches/{id:guid}/remind")]
        public Task<IActionResult> Remind(Guid id, [FromQuery] string? who) =>
            Answer(async () => new { reminded = await _service.RemindAsync(id, string.Equals(who, "notUpdated", StringComparison.OrdinalIgnoreCase)) });

        [HttpPost("links/{id:guid}/stop")]
        public Task<IActionResult> StopLink(Guid id) => Answer(() => _service.StopLinkAsync(id));

        [HttpPost("links/{id:guid}/resend")]
        public Task<IActionResult> Resend(Guid id) => Answer(() => _service.ResendAsync(id));

        private async Task<IActionResult> Answer<T>(Func<Task<T>> action)
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
