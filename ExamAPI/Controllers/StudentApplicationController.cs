using ExamAPI.Services.ExamApplications;
using ExamAPI.Services.StudentAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// The student site, exam application link (ATKT, Re-Exam, Revaluation): subjects, fee, pay, result, receipt.
    /// Student session only (<see cref="StudentAccessAuth"/>); everything is for the signed-in link's student.
    /// </summary>
    [Route("api/StudentAccess/application")]
    [ApiController]
    [Authorize(Policy = StudentAccessAuth.Policy)]
    public class StudentApplicationController : ControllerBase
    {
        private readonly IStudentExamApplicationService _service;
        private readonly StudentSiteUrl _site;

        public StudentApplicationController(IStudentExamApplicationService service, StudentSiteUrl site)
        {
            _service = service;
            _site = site;
        }

        public class SubjectsRequest
        {
            public List<Guid>? SubjectIds { get; set; }
            /// <summary>The student ticked the declaration (pay only).</summary>
            public bool Declared { get; set; }
            /// <summary>The student site the browser is on (checked against the allowed origins).</summary>
            public string? AppUrl { get; set; }
        }

        [HttpGet]
        public Task<IActionResult> Get() => Answer(() => _service.GetAsync(User));

        [HttpPost("quote")]
        public Task<IActionResult> Quote([FromBody] SubjectsRequest request) => Answer(() => _service.QuoteAsync(User, request.SubjectIds ?? new()));

        [HttpPost("pay")]
        public Task<IActionResult> Pay([FromBody] SubjectsRequest request) => Answer(() =>
        {
            var site = _site.Resolve(request.AppUrl)
                ?? throw new InvalidOperationException("The student site address isn't known to the server.");
            var apiBase = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/";
            return _service.PayAsync(User, request.SubjectIds ?? new(), request.Declared, apiBase, site);
        });

        /// <summary>Back from the gateway: re-checks the student's own open payments and answers the page.</summary>
        [HttpPost("payment")]
        public Task<IActionResult> Payment() => Answer(() => _service.PaymentResultAsync(User));

        /// <summary>The application form (the old printed form to the Principal), once paid.</summary>
        [HttpGet("form")]
        public Task<IActionResult> Form() => Document(ExamDocumentParts.Form);

        /// <summary>The fee receipt, once paid.</summary>
        [HttpGet("receipt")]
        public Task<IActionResult> Receipt() => Document(ExamDocumentParts.Receipt);

        private async Task<IActionResult> Document(ExamDocumentParts part)
        {
            try
            {
                var (pdf, name) = await _service.DocumentAsync(User, part);
                Response.Headers.CacheControl = "no-store";
                return File(pdf, "application/pdf", name);
            }
            catch (UnauthorizedAccessException) { return Unauthorized(); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        private async Task<IActionResult> Answer<T>(Func<Task<T>> action)
        {
            try
            {
                return Ok(await action());
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
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
