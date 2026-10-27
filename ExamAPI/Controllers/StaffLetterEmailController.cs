using ExamAPI.Services.Staff;
using ExamAPI.Services.StudentAccess;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// Staff letters > Email letters: the exam office previews and sends each person their letters link, and sees who
    /// got it, opened it and downloaded. Signed-in office staff only (the fallback policy); scoped to their college by
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

        /// <summary>Emails each person their link (the older link of the same person and exam stops working).</summary>
        [HttpPost]
        public Task<IActionResult> Send([FromBody] StaffEmailRequest request) => Answer(async () =>
        {
            var baseUrl = _site.Resolve(request.AppUrl)
                ?? throw new InvalidOperationException("The site address isn't known to the server. Set StudentAccess:AppUrl in the API settings.");
            return await _service.SendAsync(request, baseUrl);
        });

        /// <summary>The latest link of each person for the exam: when it was emailed, opened and first downloaded.</summary>
        [HttpGet("status")]
        public Task<IActionResult> Status([FromQuery] Guid examId) => Answer(() => _service.StatusAsync(examId));

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
