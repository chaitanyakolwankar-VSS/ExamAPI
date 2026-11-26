using ExamAPI.Models;
using ExamAPI.Services.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExamAPI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class BillingController : ControllerBase
    {
        private readonly IBillingService _service;
        private readonly ILogger<BillingController> _logger;
        private static readonly JsonSerializerOptions ClaimJsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            };

        public BillingController(IBillingService service, ILogger<BillingController> logger)
        {
            _service = service;
            _logger = logger;
        }

        private ContentResult JsonWithoutCycles(object value) =>
            Content(JsonSerializer.Serialize(value, ClaimJsonOptions), "application/json");

        public class SubmitClaimRequest
        {
            public StaffBillingClaim Claim { get; set; } = new StaffBillingClaim();
            public List<StaffBillItemDto>? PaperItems { get; set; }
        }

        public class StaffBillItemDto
        {
            public string? Id { get; set; }
            public string? ClaimId { get; set; }
            public string? SubjectName { get; set; }
            public string? Subject { get; set; }
            public string? Semester { get; set; }
            public int TheoryCount { get; set; }
            public decimal TheoryRate { get; set; }
            public int TwCount { get; set; }
            public decimal TwRate { get; set; }
            public int PracticalCount { get; set; }
            public decimal PracticalRate { get; set; }
            public int OralCount { get; set; }
            public decimal OralRate { get; set; }
            public int ProjectOralCount { get; set; }
            public decimal ProjectOralRate { get; set; }
            public int MeCount { get; set; }
            public decimal MeRate { get; set; }
            public int OwpCount { get; set; }
            public decimal OwpRate { get; set; }
            public int TtCount { get; set; }
            public decimal TtRate { get; set; }
            public decimal ItemSubtotal { get; set; }
            public decimal Total { get; set; }

            public StaffBillItem ToEntity(Guid claimId, Guid? collegeId)
            {
                var subj = !string.IsNullOrWhiteSpace(SubjectName) ? SubjectName : (Subject ?? string.Empty);
                var sem = !string.IsNullOrWhiteSpace(Semester) ? Semester : "Sem-3";
                var entity = new StaffBillItem
                {
                    Id = Guid.NewGuid(),
                    ClaimId = claimId,
                    CollegeId = collegeId,
                    SubjectName = subj,
                    Semester = sem,
                    TheoryCount = TheoryCount,
                    TheoryRate = TheoryRate,
                    TwCount = TwCount,
                    TwRate = TwRate,
                    PracticalCount = PracticalCount,
                    PracticalRate = PracticalRate,
                    OralCount = OralCount,
                    OralRate = OralRate,
                    ProjectOralCount = ProjectOralCount,
                    ProjectOralRate = ProjectOralRate,
                    MeCount = MeCount,
                    MeRate = MeRate,
                    OwpCount = OwpCount,
                    OwpRate = OwpRate,
                    TtCount = TtCount,
                    TtRate = TtRate,
                };
                var th = entity.TheoryCount * entity.TheoryRate;
                var tw = entity.TwCount * entity.TwRate;
                var pr = entity.PracticalCount * entity.PracticalRate;
                var oral = entity.OralCount * entity.OralRate;
                var projOr = entity.ProjectOralCount * entity.ProjectOralRate;
                var me = entity.MeCount * entity.MeRate;
                var tt = entity.TtCount * entity.TtRate;
                entity.ItemSubtotal = th + tw + pr + oral + projOr + me + tt;
                return entity;
            }
        }

        public class SendClaimEmailRequest
        {
            public string? ClaimId { get; set; }
            public string? Token { get; set; }
            public string? AppUrl { get; set; }
            public string? Email { get; set; }
            public string? StaffName { get; set; }
            public string? RoleType { get; set; }
            public string? SessionName { get; set; }
            public string? StaffId { get; set; }
            public string? DutyChartId { get; set; }
            public string? CollegeId { get; set; }
        }

        public class SendBatchClaimEmailRequest
        {
            public List<string> ClaimIds { get; set; } = new List<string>();
            public string? AppUrl { get; set; }
        }

        public class SendApprovedBillEmailRequest
        {
            public string? Email { get; set; }
            public string? AppUrl { get; set; }
        }

        public class VerifyOtpRequest
        {
            public string? Code { get; set; }
        }

        public class StopClaimsRequest
        {
            public List<string>? Tokens { get; set; }
            public List<Guid>? ClaimIds { get; set; }
            public Guid? DutyChartId { get; set; }
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
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
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
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
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
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
            }
        }

        // ==========================================
        // 2. FACULTY & MODERATOR CLAIM WORKFLOW
        // ==========================================

        //[HttpGet("claims")]
        //public async Task<IActionResult> GetClaims([FromQuery] Guid? dutyChartId, [FromQuery] string? status, [FromQuery] string? roleType)
        //{
        //    try
        //    {
        //        var claims = await _service.GetClaimsAsync(dutyChartId, status, roleType);
        //        return Ok(claims);
        //    }
        //    catch (Exception ex)
        //    {
        //        return BadRequest(new { message = ex.Message });
        //    }
        //}

        [HttpGet("claims")]
        public async Task<IActionResult> GetClaims([FromQuery] Guid? dutyChartId, [FromQuery] string? status, [FromQuery] string? roleType, [FromQuery] Guid? ayid, [FromQuery] Guid? collegeId)
        {
            try
            {
                var claims = await _service.GetClaimsAsync(dutyChartId, status, roleType, ayid, collegeId);

                return JsonWithoutCycles(claims);
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
            }
        }

        [HttpGet("claims/{id:guid}")]
        public async Task<IActionResult> GetClaimById(Guid id)
        {
            try
            {
                var claim = await _service.GetClaimByIdAsync(id);
                if (claim == null) return NotFound(new { message = "Claim not found." });
                return JsonWithoutCycles(claim);
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
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
                return JsonWithoutCycles(claim);
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
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
                var items = (request.PaperItems ?? new List<StaffBillItemDto>())
                    .Select(dto => dto.ToEntity(Guid.Empty, null))
                    .ToList();
                var updated = await _service.SubmitClaimAsync(token, request.Claim, items);
                return JsonWithoutCycles(updated);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
            }
        }

        [HttpPost("save-batch")]
        [HttpPost("claims/save-batch")]
        public async Task<IActionResult> SaveClaims([FromBody] List<StaffBillingClaim> claims)
        {
            if (claims == null || claims.Count == 0)
            {
                return Ok(new List<StaffBillingClaim>());
            }

            try
            {
                var saved = await _service.SaveClaimsAsync(claims);
                return JsonWithoutCycles(saved);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save claims batch: {Message}. Inner: {Inner}", ex.Message, ex.InnerException?.Message);
                return BadRequest(new { message = ex.Message, inner = ex.InnerException?.Message });
            }
        }

        [HttpPost("claims/{id}/approve")]
        [HttpPost("approve-claim/{id}")]
        public async Task<IActionResult> ApproveClaim(string id, [FromQuery] string? approvedBy, [FromQuery] string? appUrl)
        {
            try
            {
                var approver = string.IsNullOrWhiteSpace(approvedBy) ? User.Identity?.Name ?? "Exam Office" : approvedBy;
                var clientUrl = string.IsNullOrWhiteSpace(appUrl)
                    ? $"{Request.Scheme}://{Request.Host}"
                    : appUrl;
                var approved = await _service.ApproveClaimAsync(id, approver, clientUrl);
                return JsonWithoutCycles(approved);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
            }
        }

        [HttpGet("next-bill-no")]
        public async Task<IActionResult> GetNextBillNo([FromQuery] string? type = "STAFF", [FromQuery] Guid? collegeId = null)
        {
            try
            {
                var nextNo = await _service.GetNextUniqueBillNoAsync(type ?? "STAFF", collegeId);
                return Ok(new { billNo = nextNo });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = GetFullException(ex) });
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
                var items = (request.PaperItems ?? new List<StaffBillItemDto>())
                    .Select(dto => dto.ToEntity(Guid.Empty, null))
                    .ToList();
                var updated = await _service.UpdateClaimByAdminAsync(guidId, token, request.Claim, items);
                return JsonWithoutCycles(updated);
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
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
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
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

                Guid? claimIdGuid = Guid.TryParse(request.ClaimId, out var cid) ? cid : null;
                Guid? staffIdGuid = Guid.TryParse(request.StaffId, out var sid) ? sid : null;
                Guid? dutyChartIdGuid = Guid.TryParse(request.DutyChartId, out var did) ? did : null;
                Guid? collegeIdGuid = Guid.TryParse(request.CollegeId, out var colid) ? colid : null;

                // Pass CollegeId through so the service can use college-first email account selection
                var result = await _service.SendClaimEmailAsync(
                    claimIdGuid,
                    request.Token,
                    appUrl,
                    request.Email,
                    request.StaffName,
                    request.RoleType,
                    request.SessionName,
                    staffIdGuid,
                    dutyChartIdGuid,
                    collegeIdGuid
                );
                return Ok(result);
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
            }
        }

        [HttpPost("claims/{id}/send-approved-email")]
        public async Task<IActionResult> SendApprovedBillEmail(
            string id,
            [FromBody] SendApprovedBillEmailRequest? body = null,
            [FromQuery] string? email = null,
            [FromQuery] string? appUrl = null)
        {
            try
            {
                var targetEmail = !string.IsNullOrWhiteSpace(body?.Email) ? body.Email : email;
                var rawUrl = !string.IsNullOrWhiteSpace(body?.AppUrl) ? body.AppUrl : appUrl;
                var resolvedUrl = string.IsNullOrWhiteSpace(rawUrl)
                    ? $"{Request.Scheme}://{Request.Host}"
                    : rawUrl;
                Guid? guidId = Guid.TryParse(id, out var gid) ? gid : null;
                var result = await _service.SendApprovedBillEmailAsync(guidId, id, resolvedUrl, targetEmail);
                return Ok(result);
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
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
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
            }
        }

        [AllowAnonymous]
        [HttpPost("claims/{token}/open")]
        public async Task<IActionResult> OpenClaimPortal(string token)
        {
            try
            {
                var info = await _service.OpenClaimPortalAsync(token);
                return JsonWithoutCycles(info);
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
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
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
            }
        }

        [AllowAnonymous]
        [HttpPost("claims/{token}/verify-otp")]
        public async Task<IActionResult> VerifyClaimOtp(string token, [FromBody] VerifyOtpRequest request)
        {
            try
            {
                var result = await _service.VerifyClaimOtpAsync(token, request?.Code ?? string.Empty);
                return JsonWithoutCycles(result);
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: send-claim-email failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
            }
        }
        [HttpPost("claims/stop")]
        public async Task<IActionResult> StopClaims([FromBody] StopClaimsRequest request)
        {
            try
            {
                var stopped = await _service.StopClaimsAsync(request.Tokens, request.ClaimIds, request.DutyChartId);
                return Ok(new
                {
                    success = true,
                    stopped,
                    message = stopped > 0
                        ? $"Successfully stopped {stopped} claim link{(stopped == 1 ? "" : "s")}."
                        : "No active claim links were stopped."
                });
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullException(ex);
                _logger.LogError(ex, "Billing: stop-claims failed: {Message}", fullMsg);
                return BadRequest(new { message = fullMsg });
            }
        }

        private static string GetFullException(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            var cur = ex;
            while (cur != null)
            {
                if (sb.Length > 0) sb.Append(" --> ");
                sb.Append(cur.Message);
                cur = cur.InnerException;
            }
            return sb.ToString();
        }
    }
}
