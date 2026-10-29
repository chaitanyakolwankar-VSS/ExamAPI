using ExamAPI.Services.Staff;
using ExamAPI.Services.StudentAccess;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// Staff letters > Email letters: the exam office previews and sends each person their letters link (supervision letters
    /// of a duty chart, or examiner and moderator letters of an exam), and sees who got it, opened it and downloaded. Signed-in office staff only (the fallback policy); scoped to their college by
    /// the tenant filter.
    /// </summary>
    [Route("api/StaffLetters/email")]
    [ApiController]
    public class StaffLetterEmailController : ControllerBase
    {
        private readonly IStaffLinkService _service;
        private readonly StudentSiteUrl _site;

        public StaffLetterEmailController(IStaffLinkService service, StudentSiteUrl site)
        {
            _service = service;
            _site = site;
        }

        /// <summary>Who an email would reach, who has been emailed already, and whether this college can send email.</summary>
        [HttpPost("preview")]
        public Task<IActionResult> Preview([FromBody] StaffEmailRequest request) => Answer(() => _service.PreviewAsync(request));

        /// <summary>Emails each person their link (the older link of the same person and exam or duty chart stops working).</summary>
        [HttpPost]
        public Task<IActionResult> Send([FromBody] StaffEmailRequest request) => Answer(async () =>
        {
            var baseUrl = _site.Resolve(request.AppUrl)
                ?? throw new InvalidOperationException("The site address isn't known to the server. Set StudentAccess:AppUrl in the API settings.");
            return await _service.SendAsync(request, baseUrl);
        });

        /// <summary>Stops the emailed links of everyone (or only the given people) for the exam or duty chart: they can't open their letters until emailed again.</summary>
        [HttpPost("stop")]
        public Task<IActionResult> Stop([FromBody] StaffStopRequest request) => Answer(() => _service.StopLinksAsync(request));

        /// <summary>The latest link of each person for the exam or duty chart: when it was emailed, opened, first downloaded and (if the office stopped it) stopped.</summary>
        [HttpGet("status")]
        public Task<IActionResult> Status([FromQuery] Guid? examId, [FromQuery] Guid? dutyChartId) => Answer(() => _service.StatusAsync(examId, dutyChartId));

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
