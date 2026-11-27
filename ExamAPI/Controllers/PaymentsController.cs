using System.Net;
using ExamAPI.Models;
using ExamAPI.Services.ExamApplications;
using ExamAPI.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    /// <summary>
    /// Where gateways send the student's browser back (anonymous: the browser has no staff or student token here).
    /// Nothing the browser brings is trusted: the order number in the address only says which order to ask the gateway
    /// about. Also serves the test gateway's pretend payment page outside Production.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentService _payments;
        private readonly TestPaymentGateway _testGateway;
        private readonly IHostEnvironment _env;

        public PaymentsController(IPaymentService payments, TestPaymentGateway testGateway, IHostEnvironment env)
        {
            _payments = payments;
            _testGateway = testGateway;
            _env = env;
        }

        [HttpGet("return/{orderNo}")]
        [HttpPost("return/{orderNo}")]
        public async Task<IActionResult> Return(string orderNo, CancellationToken ct)
        {
            try
            {
                var order = await _payments.ConfirmAsync(orderNo, ct);
                return string.IsNullOrEmpty(order.ReturnBaseUrl)
                    ? Content(Page("Payment", $"<p>Payment status: {WebUtility.HtmlEncode(order.Status)}. You can close this window.</p>"), "text/html")
                    : Redirect(_payments.ResultUrl(order));
            }
            catch (KeyNotFoundException)
            {
                return NotFound(Page("Payment", "<p>This payment was not found.</p>"));
            }
        }

        // ---- test gateway (development and demo only) ----

        [HttpGet("test-gateway/{orderNo}")]
        public IActionResult TestGatewayPage(string orderNo)
        {
            if (_env.IsProduction()) return NotFound();
            var found = _testGateway.Find(orderNo);
            if (found == null) return NotFound(Page("Test gateway", "<p>Unknown test order.</p>"));
            var no = WebUtility.HtmlEncode(orderNo);
            var action = WebUtility.HtmlEncode(Url.Content($"~/{TestPaymentGateway.PagePath}{Uri.EscapeDataString(orderNo)}"));
            string Button(string outcome, string label) =>
                $"<button name=\"outcome\" value=\"{outcome}\" style=\"display:block;width:100%;margin:8px 0;padding:10px\">{label}</button>";
            return Content(Page("Test gateway", $@"
<p style=""color:#b45309""><b>Test gateway — no real money.</b></p>
<p>Order <b>{no}</b><br>Amount <b>₹{FeeCalculator.Money(found.Value.Amount)}</b></p>
<form method=""post"" action=""{action}"">
{Button(nameof(TestPaymentGateway.Outcome.Success), "Pay (succeeds)")}
{Button(nameof(TestPaymentGateway.Outcome.Failed), "Fail")}
{Button(nameof(TestPaymentGateway.Outcome.Pending), "Leave pending")}
{Button(nameof(TestPaymentGateway.Outcome.WrongAmount), "Succeed with a wrong amount (must not count)")}
</form>"), "text/html");
        }

        [HttpPost("test-gateway/{orderNo}")]
        [Consumes("application/x-www-form-urlencoded")]
        public IActionResult TestGatewayDecide(string orderNo, [FromForm] string outcome)
        {
            if (_env.IsProduction()) return NotFound();
            if (!Enum.TryParse<TestPaymentGateway.Outcome>(outcome, out var chosen)) return BadRequest();
            var returnUrl = _testGateway.Decide(orderNo, chosen);
            return returnUrl == null ? NotFound() : Redirect(returnUrl);
        }

        private static string Page(string title, string body) =>
            $"<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + $"<title>{WebUtility.HtmlEncode(title)}</title></head>"
            + $"<body style=\"font-family:system-ui,sans-serif;max-width:420px;margin:40px auto;padding:0 16px\"><h2>{WebUtility.HtmlEncode(title)}</h2>{body}</body></html>";
    }
}
