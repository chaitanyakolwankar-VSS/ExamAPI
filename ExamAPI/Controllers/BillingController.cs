using ExamAPI.Models;
using ExamAPI.Services.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BillingController : ControllerBase
    {
        private readonly IBillingService _service;

        public BillingController(IBillingService service)
        {
            _service = service;
        }

        public class SubmitClaimRequest
        {
            public StaffBillingClaim Claim { get; set; } = new StaffBillingClaim();
            public List<StaffBillItem> PaperItems { get; set; } = new List<StaffBillItem>();
        }

        public class SendClaimEmailRequest
        {
            public Guid? ClaimId { get; set; }
            public string? Token { get; set; }
            public string? AppUrl { get; set; }
            public string? Email { get; set; }
            public string? StaffName { get; set; }
            public string? RoleType { get; set; }
            public string? SessionName { get; set; }
            public Guid? StaffId { get; set; }
            public Guid? DutyChartId { get; set; }
        }

        public class SendBatchClaimEmailRequest
        {
            public List<string> ClaimIds { get; set; } = new List<string>();
            public string? AppUrl { get; set; }
        }

        public class VerifyOtpRequest
        {
            public string? Code { get; set; }
        }

        // ==========================================
        // 1. PRICE SETS & REMUNERATION RATES
        // ==========================================

        [HttpGet("pricesets")]
        public async Task<IActionResult> GetPriceSets()
        {
            try
            {
                var sets = await _service.GetPriceSetsAsync();
                return Ok(sets);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("pricesets")]
        public async Task<IActionResult> SavePriceSet([FromBody] BillingPriceSet priceSet)
        {
            try
            {
                var saved = await _service.SavePriceSetAsync(priceSet);
                return Ok(saved);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("pricesets/{id:guid}")]
        public async Task<IActionResult> DeletePriceSet(Guid id)
        {
            try
            {
                var deleted = await _service.DeletePriceSetAsync(id);
                return Ok(new { success = deleted });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ==========================================
        // 2. FACULTY & MODERATOR CLAIM WORKFLOW
        // ==========================================

        [HttpGet("claims")]
        public async Task<IActionResult> GetClaims([FromQuery] Guid? dutyChartId, [FromQuery] string? status, [FromQuery] string? roleType)
        {
            try
            {
                var claims = await _service.GetClaimsAsync(dutyChartId, status, roleType);
                return Ok(claims);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("claims/{id:guid}")]
        public async Task<IActionResult> GetClaimById(Guid id)
        {
            try
            {
                var claim = await _service.GetClaimByIdAsync(id);
                if (claim == null) return NotFound(new { message = "Claim not found." });
                return Ok(claim);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Public token lookup for faculty & external moderators opening their unique claim link.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("claims/by-token/{token}")]
        public async Task<IActionResult> GetClaimByToken(string token)
        {
            try
            {
                var claim = await _service.GetClaimByTokenAsync(token);
                if (claim == null) return NotFound(new { message = "Invalid or expired claim token." });
                return Ok(claim);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Direct claim submission by faculty or external moderator via their unique link token.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("claims/submit/{token}")]
        public async Task<IActionResult> SubmitClaim(string token, [FromBody] SubmitClaimRequest request)
        {
            try
            {
                var updated = await _service.SubmitClaimAsync(token, request.Claim, request.PaperItems ?? new List<StaffBillItem>());
                return Ok(updated);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("save-batch")]
        [HttpPost("claims/save-batch")]
        public async Task<IActionResult> SaveClaims([FromBody] List<StaffBillingClaim> claims)
        {
            try
            {
                var saved = await _service.SaveClaimsAsync(claims);
                return Ok(saved);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("claims/{id}/approve")]
        [HttpPost("approve-claim/{id}")]
        public async Task<IActionResult> ApproveClaim(string id, [FromQuery] string? approvedBy)
        {
            try
            {
                var approver = string.IsNullOrWhiteSpace(approvedBy) ? User.Identity?.Name ?? "Exam Office" : approvedBy;
                var approved = await _service.ApproveClaimAsync(id, approver);
                return Ok(approved);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Flexible admin update: accepts GUID id, token, or creates a new claim (UPSERT).
        /// </summary>
        [HttpPut("claims/{id}")]
        public async Task<IActionResult> UpdateClaimByAdmin(string id, [FromBody] SubmitClaimRequest request)
        {
            try
            {
                Guid? guidId = Guid.TryParse(id, out var parsed) ? parsed : null;
                var token = guidId == null ? id : null;
                var updated = await _service.UpdateClaimByAdminAsync(guidId, token, request.Claim, request.PaperItems ?? new List<StaffBillItem>());
                return Ok(updated);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("sync-from-chart/{dutyChartId:guid}")]
        public async Task<IActionResult> SyncFromDutyChart(Guid dutyChartId)
        {
            try
            {
                var synced = await _service.SyncFromDutyChartAsync(dutyChartId);
                return Ok(synced);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ==========================================
        // 3. EMAIL DISPATCH & OTP VERIFICATION
        // ==========================================

        [HttpPost("send-claim-email")]
        public async Task<IActionResult> SendClaimEmail([FromBody] SendClaimEmailRequest request)
        {
            try
            {
                var appUrl = string.IsNullOrWhiteSpace(request.AppUrl)
                    ? $"{Request.Scheme}://{Request.Host}"
                    : request.AppUrl;
                var result = await _service.SendClaimEmailAsync(
                    request.ClaimId,
                    request.Token,
                    appUrl,
                    request.Email,
                    request.StaffName,
                    request.RoleType,
                    request.SessionName,
                    request.StaffId,
                    request.DutyChartId
                );
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("send-claim-email-batch")]
        public async Task<IActionResult> SendBatchClaimEmails([FromBody] SendBatchClaimEmailRequest request)
        {
            try
            {
                var appUrl = string.IsNullOrWhiteSpace(request.AppUrl)
                    ? $"{Request.Scheme}://{Request.Host}"
                    : request.AppUrl;
                var result = await _service.SendBatchClaimEmailsAsync(request.ClaimIds ?? new List<string>(), appUrl);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpPost("claims/{token}/open")]
        public async Task<IActionResult> OpenClaimPortal(string token)
        {
            try
            {
                var info = await _service.OpenClaimPortalAsync(token);
                return Ok(info);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpPost("claims/{token}/send-otp")]
        public async Task<IActionResult> SendClaimOtp(string token)
        {
            try
            {
                var result = await _service.SendClaimOtpAsync(token);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpPost("claims/{token}/verify-otp")]
        public async Task<IActionResult> VerifyClaimOtp(string token, [FromBody] VerifyOtpRequest request)
        {
            try
            {
                var result = await _service.VerifyClaimOtpAsync(token, request?.Code ?? string.Empty);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
