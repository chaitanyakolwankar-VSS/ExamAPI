using ExamAPI.Models;

namespace ExamAPI.Services.Billing
{
    public class ClaimEmailResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SentCount { get; set; }
        public int FailedCount { get; set; }
    }

    public class ClaimPortalInfo
    {
        public bool Exists { get; set; }
        public string StaffName { get; set; } = string.Empty;
        public string RoleType { get; set; } = "supervisor";
        public string SessionName { get; set; } = string.Empty;
        public string MaskedEmail { get; set; } = string.Empty;
        public string Status { get; set; } = "NOT_SENT";
        public string Message { get; set; } = string.Empty;
        public bool IsSubmitted { get; set; }
        public bool IsApproved { get; set; }
        public bool IsOtpVerified { get; set; }
        public StaffBillingClaim? Claim { get; set; }
    }

    public class ClaimOtpResult
    {
        public bool Success { get; set; }
        public string MaskedEmail { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class VerifyClaimOtpResult
    {
        public bool Success { get; set; }
        public bool Verified { get; set; }
        public string Message { get; set; } = string.Empty;
        public StaffBillingClaim? Claim { get; set; }
    }

    public interface IBillingService
    {
        Task<List<BillingPriceSet>> GetPriceSetsAsync();
        Task<BillingPriceSet> SavePriceSetAsync(BillingPriceSet priceSet);
        Task<bool> DeletePriceSetAsync(Guid id);
        Task<List<StaffBillingClaim>> GetClaimsAsync(Guid? dutyChartId = null, string? status = null, string? roleType = null);
        Task<StaffBillingClaim?> GetClaimByIdAsync(Guid id);
        Task<StaffBillingClaim?> GetClaimByTokenAsync(string token);
        Task<StaffBillingClaim> SubmitClaimAsync(string token, StaffBillingClaim claimUpdate, List<StaffBillItem> items);
        Task<StaffBillingClaim> UpdateClaimByAdminAsync(Guid? id, string? token, StaffBillingClaim claimUpdate, List<StaffBillItem> items);
        Task<StaffBillingClaim> ApproveClaimAsync(string idOrToken, string approvedBy, string? appUrl = null);
        Task<StaffBillingClaim> ApproveClaimAsync(Guid id, string approvedBy, string? appUrl = null);
        Task<List<StaffBillingClaim>> SyncFromDutyChartAsync(Guid dutyChartId);
        Task<List<StaffBillingClaim>> SaveClaimsAsync(List<StaffBillingClaim> claims);

        // Email dispatch & OTP verification
        Task<ClaimEmailResult> SendClaimEmailAsync(Guid? claimId, string? token, string appUrl, string? email = null, string? staffName = null, string? roleType = null, string? sessionName = null, Guid? staffId = null, Guid? dutyChartId = null, Guid? collegeId = null);
        Task<ClaimEmailResult> SendBatchClaimEmailsAsync(List<string> claimIdsOrTokens, string appUrl);
        Task<ClaimEmailResult> SendBatchClaimEmailsAsync(List<Guid> claimIds, string appUrl);
        Task<ClaimPortalInfo> OpenClaimPortalAsync(string token);
        Task<ClaimOtpResult> SendClaimOtpAsync(string token);
        Task<VerifyClaimOtpResult> VerifyClaimOtpAsync(string token, string code);
        Task<int> StopClaimsAsync(List<string>? tokens = null, List<Guid>? claimIds = null, Guid? dutyChartId = null);
    }
}
