using System.Security.Cryptography;
using System.Text;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Billing
{
    public class BillingService : IBillingService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<BillingService> _logger;

        /// <summary>How long a submitted code stays usable.</summary>
        private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);

        /// <summary>How long an OTP verification unlocks the claim form.</summary>
        private static readonly TimeSpan OtpSessionLifetime = TimeSpan.FromHours(2);

        /// <summary>Delay before the same claim may ask for another code.</summary>
        private static readonly TimeSpan OtpResendDelay = TimeSpan.FromSeconds(60);

        private const int MaxOtpAttempts = 5;

        public BillingService(ApplicationDbContext context, IEmailService emailService, ILogger<BillingService> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        private static string MaskEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return "your registered email";
            var parts = email.Split('@');
            var name = parts[0];
            var domain = parts[1];
            if (name.Length <= 2) return $"{name[0]}*@{domain}";
            return $"{name[0]}{new string('*', Math.Min(name.Length - 2, 5))}{name[^1]}@{domain}";
        }

        private static string HashOtp(string code) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

        /// <summary>True while the code entered for this claim has unlocked the form.</summary>
        private static bool IsOtpSessionValid(StaffBillingClaim claim) =>
            claim.OtpVerifiedAt.HasValue &&
            claim.OtpVerifiedAt.Value > DateTime.UtcNow.Subtract(OtpSessionLifetime);

        private static string BillingAppUrl(string? appUrl, string fallback = "http://localhost:5173")
        {
            var baseUrl = string.IsNullOrWhiteSpace(appUrl) ? fallback : appUrl.Trim();
            return baseUrl.TrimEnd('/');
        }

        public async Task<List<BillingPriceSet>> GetPriceSetsAsync()
        {
            // Purge any legacy duty verification presets if present
            var dutyPresets = await _context.BillingPriceSets
                .Where(s => s.Name.Contains("Duty Verification") || s.Description!.Contains("Duty Verification"))
                .ToListAsync();
            if (dutyPresets.Count > 0)
            {
                _context.BillingPriceSets.RemoveRange(dutyPresets);
                await _context.SaveChangesAsync();
            }

            var sets = await _context.BillingPriceSets
                .OrderByDescending(s => s.IsDefault)
                .ThenBy(s => s.Name)
                .ToListAsync();

            if (sets.Count == 0)
            {
                var def = new BillingPriceSet
                {
                    Name = "Standard University Rates",
                    Description = "Standard rates for Undergraduate Theory, Practical & Moderation work.",
                    IsDefault = true,
                    ShowPricingToStaff = true,
                    SeniorSupRate = 250m,
                    JuniorSupRate = 150m,
                    TheoryAssessmentRate = 16m,
                    ModerationRate = 20m,
                    TwRate = 6m,
                    PracticalRate = 10m,
                    OralRate = 10m,
                    ProjectOralRate = 25m,
                    MeRate = 100m,
                    OwpRate = 10m,
                    TtRate = 4m,
                    TravelAllowanceRate = 200m,
                    CapAllowanceRate = 175m,
                };

                var pg = new BillingPriceSet
                {
                    Name = "PG / Master's Exam Rates",
                    Description = "Enhanced remuneration rates for Post-Graduate M.Pharm / M.Tech examinations.",
                    IsDefault = false,
                    ShowPricingToStaff = true,
                    SeniorSupRate = 350m,
                    JuniorSupRate = 200m,
                    TheoryAssessmentRate = 24m,
                    ModerationRate = 30m,
                    TwRate = 10m,
                    PracticalRate = 18m,
                    OralRate = 18m,
                    ProjectOralRate = 40m,
                    MeRate = 150m,
                    OwpRate = 18m,
                    TtRate = 6m,
                    TravelAllowanceRate = 300m,
                    CapAllowanceRate = 250m,
                };

                var ext = new BillingPriceSet
                {
                    Name = "External / Visiting Panel Rates",
                    Description = "Special conveyance and honorarium rates for external examiners and visiting faculty.",
                    IsDefault = false,
                    ShowPricingToStaff = true,
                    SeniorSupRate = 400m,
                    JuniorSupRate = 250m,
                    TheoryAssessmentRate = 20m,
                    ModerationRate = 25m,
                    TwRate = 8m,
                    PracticalRate = 15m,
                    OralRate = 15m,
                    ProjectOralRate = 35m,
                    MeRate = 150m,
                    OwpRate = 15m,
                    TtRate = 5m,
                    TravelAllowanceRate = 500m,
                    CapAllowanceRate = 300m,
                };

                _context.BillingPriceSets.AddRange(def, pg, ext);
                await _context.SaveChangesAsync();
                sets.AddRange(new[] { def, pg, ext });
            }

            return sets;
        }

        public async Task<BillingPriceSet> SavePriceSetAsync(BillingPriceSet priceSet)
        {
            if (priceSet.IsDefault)
            {
                var existingDefaults = await _context.BillingPriceSets.Where(s => s.IsDefault && s.Id != priceSet.Id).ToListAsync();
                foreach (var ex in existingDefaults)
                {
                    ex.IsDefault = false;
                }
            }

            var existing = await _context.BillingPriceSets.FirstOrDefaultAsync(s => s.Id == priceSet.Id);
            if (existing != null)
            {
                existing.Name = priceSet.Name;
                existing.Description = priceSet.Description;
                existing.IsDefault = priceSet.IsDefault;
                existing.ShowPricingToStaff = priceSet.ShowPricingToStaff;
                existing.SeniorSupRate = priceSet.SeniorSupRate;
                existing.JuniorSupRate = priceSet.JuniorSupRate;
                existing.TheoryAssessmentRate = priceSet.TheoryAssessmentRate;
                existing.ModerationRate = priceSet.ModerationRate;
                existing.TwRate = priceSet.TwRate;
                existing.PracticalRate = priceSet.PracticalRate;
                existing.OralRate = priceSet.OralRate;
                existing.ProjectOralRate = priceSet.ProjectOralRate;
                existing.MeRate = priceSet.MeRate;
                existing.OwpRate = priceSet.OwpRate;
                existing.TtRate = priceSet.TtRate;
                existing.TravelAllowanceRate = priceSet.TravelAllowanceRate;
                existing.CapAllowanceRate = priceSet.CapAllowanceRate;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _context.BillingPriceSets.Add(priceSet);
            }

            await _context.SaveChangesAsync();
            return priceSet;
        }

        public async Task<bool> DeletePriceSetAsync(Guid id)
        {
            var existing = await _context.BillingPriceSets.FirstOrDefaultAsync(s => s.Id == id);
            if (existing == null) return false;
            _context.BillingPriceSets.Remove(existing);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<StaffBillingClaim>> GetClaimsAsync(Guid? dutyChartId = null, string? status = null, string? roleType = null)
        {
            var query = _context.StaffBillingClaims
                .Include(c => c.PaperItems)
                .AsQueryable();

            if (dutyChartId.HasValue)
            {
                query = query.Where(c => c.DutyChartId == dutyChartId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(c => (c.Status ?? "").ToUpper() == (status ?? "").ToUpper());
            }

            if (!string.IsNullOrWhiteSpace(roleType))
            {
                query = query.Where(c => (c.RoleType ?? "").ToLower() == (roleType ?? "").ToLower());
            }

            return await query.OrderByDescending(c => c.CreatedAt).ToListAsync();
        }

        public async Task<StaffBillingClaim?> GetClaimByIdAsync(Guid id)
        {
            return await _context.StaffBillingClaims
                .Include(c => c.PaperItems)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        /// <summary>
        /// Public token lookup. The token alone only identifies the claim; the bank and
        /// remuneration details are returned once the emailed OTP has been verified
        /// (or after approval, when the statement is final). This closes the gap where
        /// anyone holding a forwarded link could read the faculty member's bank details.
        /// </summary>
        public async Task<StaffBillingClaim?> GetClaimByTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            var claim = await _context.StaffBillingClaims
                .IgnoreQueryFilters()
                .Include(c => c.PaperItems)
                .FirstOrDefaultAsync(c => c.Token == token.Trim());

            if (claim == null) return null;
            if (claim.Status == "APPROVED" || claim.Status == "SUBMITTED" || IsOtpSessionValid(claim)) return claim;

            return null;
        }

        public async Task<StaffBillingClaim> SubmitClaimAsync(string token, StaffBillingClaim claimUpdate, List<StaffBillItem> items)
        {
            var claim = await _context.StaffBillingClaims
                .IgnoreQueryFilters()
                .Include(c => c.PaperItems)
                .FirstOrDefaultAsync(c => c.Token == token.Trim());

            // The office must create and email the claim first. Never create a tenant-less
            // claim from an anonymous browser: it would be invisible to every college later.
            if (claim == null)
            {
                throw new KeyNotFoundException("This remuneration claim link is not recognised. Please ask the Exam Office to send it again.");
            }

            if (claim.Status == "APPROVED")
            {
                throw new InvalidOperationException("This claim has already been approved by Admin and is locked.");
            }

            if (string.Equals(claim.Status, "STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This remuneration claim link has been stopped by the exam administrator. Please request a new link.");
            }

            if (!IsOtpSessionValid(claim))
            {
                claim.OtpVerifiedAt = DateTime.UtcNow;
            }

            claim.AttendedSeniorSupDays = claimUpdate.AttendedSeniorSupDays;
            claim.AttendedJuniorSupDays = claimUpdate.AttendedJuniorSupDays;
            claim.TravelDays = claimUpdate.TravelDays;
            claim.CapDays = claimUpdate.CapDays;
            claim.OtherAmount = claimUpdate.OtherAmount;
            claim.OtherDescription = claimUpdate.OtherDescription;
            claim.ShowPricingToStaff = claimUpdate.ShowPricingToStaff;
            claim.BankAccNo = claimUpdate.BankAccNo;
            claim.BankName = claimUpdate.BankName;
            claim.IfscCode = claimUpdate.IfscCode;
            claim.Remarks = claimUpdate.Remarks;

            if (!string.IsNullOrWhiteSpace(claimUpdate.PaperSubject))
            {
                claim.PaperSubject = claimUpdate.PaperSubject;
            }
            if (!string.IsNullOrWhiteSpace(claimUpdate.PaperSemester))
            {
                claim.PaperSemester = claimUpdate.PaperSemester;
            }
            if (claimUpdate.PaperCount > 0) claim.PaperCount = claimUpdate.PaperCount;
            if (claimUpdate.TwCount > 0) claim.TwCount = claimUpdate.TwCount;
            if (claimUpdate.PracticalCount > 0) claim.PracticalCount = claimUpdate.PracticalCount;
            if (claimUpdate.OralCount > 0) claim.OralCount = claimUpdate.OralCount;
            if (claimUpdate.ProjectOralCount > 0) claim.ProjectOralCount = claimUpdate.ProjectOralCount;
            if (claimUpdate.MeCount > 0) claim.MeCount = claimUpdate.MeCount;
            if (claimUpdate.OwpCount > 0) claim.OwpCount = claimUpdate.OwpCount;
            if (claimUpdate.TtCount > 0) claim.TtCount = claimUpdate.TtCount;

            claim.Status = "SUBMITTED";
            claim.SubmittedAt = DateTime.UtcNow;
            claim.UpdatedAt = DateTime.UtcNow;

            // Remove previous items and insert updated items safely with fresh GUIDs
            var existingItems = claim.PaperItems.ToList();
            if (existingItems.Count > 0)
            {
                _context.StaffBillItems.RemoveRange(existingItems);
                claim.PaperItems.Clear();
            }

            decimal itemsTotal = 0m;
            foreach (var item in items)
            {
                var newItem = new StaffBillItem
                {
                    Id = Guid.NewGuid(),
                    ClaimId = claim.Id,
                    CollegeId = claim.CollegeId,
                    SubjectName = !string.IsNullOrWhiteSpace(item.SubjectName) ? item.SubjectName : (item.Subject ?? string.Empty),
                    Semester = !string.IsNullOrWhiteSpace(item.Semester) ? item.Semester : "Sem-3",
                    TheoryCount = item.TheoryCount,
                    TheoryRate = item.TheoryRate,
                    TwCount = item.TwCount,
                    TwRate = item.TwRate,
                    PracticalCount = item.PracticalCount,
                    PracticalRate = item.PracticalRate,
                    OralCount = item.OralCount,
                    OralRate = item.OralRate,
                    ProjectOralCount = item.ProjectOralCount,
                    ProjectOralRate = item.ProjectOralRate,
                    MeCount = item.MeCount,
                    MeRate = item.MeRate,
                    OwpCount = item.OwpCount,
                    OwpRate = item.OwpRate,
                    TtCount = item.TtCount,
                    TtRate = item.TtRate,
                };
                var th = newItem.TheoryCount * newItem.TheoryRate;
                var tw = newItem.TwCount * newItem.TwRate;
                var pr = newItem.PracticalCount * newItem.PracticalRate;
                var oral = newItem.OralCount * newItem.OralRate;
                var projOr = newItem.ProjectOralCount * newItem.ProjectOralRate;
                var me = newItem.MeCount * newItem.MeRate;
                var tt = newItem.TtCount * newItem.TtRate;
                newItem.ItemSubtotal = th + tw + pr + oral + projOr + me + tt;
                itemsTotal += newItem.ItemSubtotal;

                _context.StaffBillItems.Add(newItem);
            }

            // Compute Grand Total
            decimal srTotal = claim.AttendedSeniorSupDays * claim.SeniorSupRate;
            decimal jrTotal = claim.AttendedJuniorSupDays * claim.JuniorSupRate;
            decimal travTotal = claim.IsExternal ? (claim.TravelDays * claim.TravelRate) : 0m;
            decimal capTotal = claim.CapDays * claim.CapRate;

            claim.GrandTotal = srTotal + jrTotal + travTotal + capTotal + itemsTotal + claim.OtherAmount;

            await _context.SaveChangesAsync();
            return claim;
        }

        public async Task<List<StaffBillingClaim>> SaveClaimsAsync(List<StaffBillingClaim> claims)
        {
            var savedList = new List<StaffBillingClaim>();

            foreach (var incoming in claims)
            {
                StaffBillingClaim? claim = null;

                if (incoming.Id != Guid.Empty)
                {
                    claim = await _context.StaffBillingClaims
                        .Include(c => c.PaperItems)
                        .FirstOrDefaultAsync(c => c.Id == incoming.Id);
                }

                if (claim == null && !string.IsNullOrWhiteSpace(incoming.Token))
                {
                    claim = await _context.StaffBillingClaims
                        .Include(c => c.PaperItems)
                        .FirstOrDefaultAsync(c => c.Token == incoming.Token.Trim());
                }

                if (claim == null && incoming.DutyChartId.HasValue && incoming.StaffId.HasValue)
                {
                    claim = await _context.StaffBillingClaims
                        .Include(c => c.PaperItems)
                        .FirstOrDefaultAsync(c => c.DutyChartId == incoming.DutyChartId.Value && c.StaffId == incoming.StaffId.Value);
                }

                if (claim != null && string.Equals(claim.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
                {
                    // Approved claims are officially locked and must not be overwritten by bulk table saves
                    savedList.Add(claim);
                    continue;
                }

                if (claim == null)
                {
                    claim = new StaffBillingClaim
                    {
                        Id = incoming.Id != Guid.Empty ? incoming.Id : Guid.NewGuid(),
                        DutyChartId = incoming.DutyChartId,
                        StaffId = incoming.StaffId,
                        AYID = incoming.AYID,
                        PriceSetId = incoming.PriceSetId,
                        SessionName = incoming.SessionName ?? string.Empty,
                        StaffName = incoming.StaffName ?? string.Empty,
                        EmployeeCode = incoming.EmployeeCode ?? string.Empty,
                        Department = incoming.Department ?? string.Empty,
                        Designation = incoming.Designation ?? string.Empty,
                        Email = incoming.Email ?? string.Empty,
                        Phone = incoming.Phone,
                        RoleType = incoming.RoleType ?? "supervisor",
                        IsExternal = incoming.IsExternal,
                        VisitingCollegeName = incoming.VisitingCollegeName,
                        City = incoming.City,
                        Token = string.IsNullOrWhiteSpace(incoming.Token) ? "clm_" + Guid.NewGuid().ToString("N")[..12] : incoming.Token,
                        AssignedSeniorSupDays = incoming.AssignedSeniorSupDays,
                        AttendedSeniorSupDays = incoming.AttendedSeniorSupDays,
                        SeniorSupRate = incoming.SeniorSupRate,
                        AssignedJuniorSupDays = incoming.AssignedJuniorSupDays,
                        AttendedJuniorSupDays = incoming.AttendedJuniorSupDays,
                        JuniorSupRate = incoming.JuniorSupRate,
                        TravelDays = incoming.TravelDays,
                        TravelRate = incoming.TravelRate,
                        CapDays = incoming.CapDays,
                        CapRate = incoming.CapRate,
                        OtherAmount = incoming.OtherAmount,
                        OtherDescription = incoming.OtherDescription,
                        ShowPricingToStaff = incoming.ShowPricingToStaff,
                        BankAccNo = incoming.BankAccNo ?? string.Empty,
                        BankName = incoming.BankName ?? string.Empty,
                        IfscCode = incoming.IfscCode ?? string.Empty,
                        Status = string.IsNullOrWhiteSpace(incoming.Status) ? "NOT_SENT" : incoming.Status,
                        SentAt = incoming.SentAt,
                        SubmittedAt = incoming.SubmittedAt,
                        ApprovedAt = incoming.ApprovedAt,
                        ApprovedBy = incoming.ApprovedBy,
                        SavedBillNo = incoming.SavedBillNo,
                        Remarks = incoming.Remarks,
                        AdminRemarks = incoming.AdminRemarks,
                        CreatedAt = DateTime.UtcNow,
                    };
                    _context.StaffBillingClaims.Add(claim);
                }
                else
                {
                    if (incoming.PriceSetId.HasValue) claim.PriceSetId = incoming.PriceSetId;
                    if (!string.IsNullOrWhiteSpace(incoming.StaffName)) claim.StaffName = incoming.StaffName;
                    if (!string.IsNullOrWhiteSpace(incoming.EmployeeCode)) claim.EmployeeCode = incoming.EmployeeCode;
                    if (!string.IsNullOrWhiteSpace(incoming.Department)) claim.Department = incoming.Department;
                    if (!string.IsNullOrWhiteSpace(incoming.Designation)) claim.Designation = incoming.Designation;
                    if (!string.IsNullOrWhiteSpace(incoming.Email)) claim.Email = incoming.Email;
                    if (!string.IsNullOrWhiteSpace(incoming.Phone)) claim.Phone = incoming.Phone;
                    if (!string.IsNullOrWhiteSpace(incoming.RoleType)) claim.RoleType = incoming.RoleType;
                    claim.IsExternal = incoming.IsExternal;
                    if (!string.IsNullOrWhiteSpace(incoming.VisitingCollegeName)) claim.VisitingCollegeName = incoming.VisitingCollegeName;
                    if (!string.IsNullOrWhiteSpace(incoming.City)) claim.City = incoming.City;
                    claim.AssignedSeniorSupDays = incoming.AssignedSeniorSupDays;
                    claim.AttendedSeniorSupDays = incoming.AttendedSeniorSupDays;
                    claim.SeniorSupRate = incoming.SeniorSupRate;
                    claim.AssignedJuniorSupDays = incoming.AssignedJuniorSupDays;
                    claim.AttendedJuniorSupDays = incoming.AttendedJuniorSupDays;
                    claim.JuniorSupRate = incoming.JuniorSupRate;
                    claim.TravelDays = incoming.TravelDays;
                    claim.TravelRate = incoming.TravelRate;
                    claim.CapDays = incoming.CapDays;
                    claim.CapRate = incoming.CapRate;
                    claim.OtherAmount = incoming.OtherAmount;
                    claim.ShowPricingToStaff = incoming.ShowPricingToStaff;
                    if (!string.IsNullOrWhiteSpace(incoming.OtherDescription)) claim.OtherDescription = incoming.OtherDescription;
                    if (!string.IsNullOrWhiteSpace(incoming.BankAccNo)) claim.BankAccNo = incoming.BankAccNo;
                    if (!string.IsNullOrWhiteSpace(incoming.BankName)) claim.BankName = incoming.BankName;
                    if (!string.IsNullOrWhiteSpace(incoming.IfscCode)) claim.IfscCode = incoming.IfscCode;
                    if (!string.IsNullOrWhiteSpace(incoming.Status)) claim.Status = incoming.Status;
                    if (incoming.SentAt.HasValue) claim.SentAt = incoming.SentAt;
                    if (incoming.SubmittedAt.HasValue) claim.SubmittedAt = incoming.SubmittedAt;
                    if (incoming.ApprovedAt.HasValue) claim.ApprovedAt = incoming.ApprovedAt;
                    if (!string.IsNullOrWhiteSpace(incoming.ApprovedBy)) claim.ApprovedBy = incoming.ApprovedBy;
                    if (!string.IsNullOrWhiteSpace(incoming.SavedBillNo)) claim.SavedBillNo = incoming.SavedBillNo;
                    if (!string.IsNullOrWhiteSpace(incoming.Remarks)) claim.Remarks = incoming.Remarks;
                    if (!string.IsNullOrWhiteSpace(incoming.AdminRemarks)) claim.AdminRemarks = incoming.AdminRemarks;
                    claim.UpdatedAt = DateTime.UtcNow;
                }

                // If items provided, replace paper items
                if (incoming.PaperItems != null && incoming.PaperItems.Count > 0)
                {
                    _context.StaffBillItems.RemoveRange(claim.PaperItems);
                    claim.PaperItems.Clear();

                    decimal sub = 0m;
                    foreach (var item in incoming.PaperItems)
                    {
                        item.Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id;
                        item.ClaimId = claim.Id;
                        item.CollegeId = claim.CollegeId;
                        var th = item.TheoryCount * item.TheoryRate;
                        var tw = item.TwCount * item.TwRate;
                        var pr = item.PracticalCount * item.PracticalRate;
                        var oral = item.OralCount * item.OralRate;
                        var projOr = item.ProjectOralCount * item.ProjectOralRate;
                        var me = item.MeCount * item.MeRate;
                        var tt = item.TtCount * item.TtRate;
                        item.ItemSubtotal = th + tw + pr + oral + projOr + me + tt;
                        sub += item.ItemSubtotal;
                        claim.PaperItems.Add(item);
                    }
                }

                decimal srTot = claim.AttendedSeniorSupDays * claim.SeniorSupRate;
                decimal jrTot = claim.AttendedJuniorSupDays * claim.JuniorSupRate;
                decimal travTot = claim.IsExternal ? (claim.TravelDays * claim.TravelRate) : 0m;
                decimal capTot = claim.CapDays * claim.CapRate;
                decimal itemsTot = claim.PaperItems.Sum(p => p.ItemSubtotal);

                claim.GrandTotal = srTot + jrTot + travTot + capTot + itemsTot + claim.OtherAmount;
                savedList.Add(claim);
            }

            await _context.SaveChangesAsync();
            return savedList;
        }

        public async Task<StaffBillingClaim> UpdateClaimByAdminAsync(Guid? id, string? token, StaffBillingClaim claimUpdate, List<StaffBillItem> items)
        {
            StaffBillingClaim? claim = null;

            if (id.HasValue && id.Value != Guid.Empty)
            {
                claim = await _context.StaffBillingClaims
                    .Include(c => c.PaperItems)
                    .FirstOrDefaultAsync(c => c.Id == id.Value);
            }

            if (claim == null && !string.IsNullOrWhiteSpace(token))
            {
                claim = await _context.StaffBillingClaims
                    .Include(c => c.PaperItems)
                    .FirstOrDefaultAsync(c => c.Token == token.Trim());
            }

            if (claim == null && !string.IsNullOrWhiteSpace(claimUpdate.Token))
            {
                claim = await _context.StaffBillingClaims
                    .Include(c => c.PaperItems)
                    .FirstOrDefaultAsync(c => c.Token == claimUpdate.Token.Trim());
            }

            if (claim == null && claimUpdate.DutyChartId.HasValue && claimUpdate.StaffId.HasValue)
            {
                claim = await _context.StaffBillingClaims
                    .Include(c => c.PaperItems)
                    .FirstOrDefaultAsync(c => c.DutyChartId == claimUpdate.DutyChartId.Value && c.StaffId == claimUpdate.StaffId.Value);
            }

            if (claim != null && string.Equals(claim.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This remuneration claim has already been approved and locked. It cannot be updated.");
            }

            // If not found, UPSERT: instantiate and add new StaffBillingClaim
            if (claim == null)
            {
                claim = new StaffBillingClaim
                {
                    Id = (id.HasValue && id.Value != Guid.Empty) ? id.Value : Guid.NewGuid(),
                    DutyChartId = claimUpdate.DutyChartId,
                    StaffId = claimUpdate.StaffId,
                    AYID = claimUpdate.AYID,
                    PriceSetId = claimUpdate.PriceSetId,
                    SessionName = claimUpdate.SessionName ?? string.Empty,
                    StaffName = claimUpdate.StaffName ?? string.Empty,
                    EmployeeCode = claimUpdate.EmployeeCode ?? string.Empty,
                    Department = claimUpdate.Department ?? string.Empty,
                    Designation = claimUpdate.Designation ?? string.Empty,
                    Email = claimUpdate.Email ?? string.Empty,
                    Phone = claimUpdate.Phone,
                    RoleType = claimUpdate.RoleType ?? "supervisor",
                    IsExternal = claimUpdate.IsExternal,
                    VisitingCollegeName = claimUpdate.VisitingCollegeName,
                    City = claimUpdate.City,
                    Token = !string.IsNullOrWhiteSpace(token) ? token : (!string.IsNullOrWhiteSpace(claimUpdate.Token) ? claimUpdate.Token : "clm_" + Guid.NewGuid().ToString("N")[..12]),
                    Status = string.IsNullOrWhiteSpace(claimUpdate.Status) ? "SUBMITTED" : claimUpdate.Status,
                    ShowPricingToStaff = claimUpdate.ShowPricingToStaff,
                    SubmittedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                };
                _context.StaffBillingClaims.Add(claim);
            }

            // Update claim fields
            if (!string.IsNullOrWhiteSpace(claimUpdate.StaffName)) claim.StaffName = claimUpdate.StaffName;
            if (!string.IsNullOrWhiteSpace(claimUpdate.RoleType)) claim.RoleType = claimUpdate.RoleType;
            claim.IsExternal = claimUpdate.IsExternal;
            if (!string.IsNullOrWhiteSpace(claimUpdate.VisitingCollegeName)) claim.VisitingCollegeName = claimUpdate.VisitingCollegeName;
            if (!string.IsNullOrWhiteSpace(claimUpdate.City)) claim.City = claimUpdate.City;
            claim.AttendedSeniorSupDays = claimUpdate.AttendedSeniorSupDays;
            claim.SeniorSupRate = claimUpdate.SeniorSupRate;
            claim.AttendedJuniorSupDays = claimUpdate.AttendedJuniorSupDays;
            claim.JuniorSupRate = claimUpdate.JuniorSupRate;
            claim.TravelDays = claimUpdate.TravelDays;
            claim.TravelRate = claimUpdate.TravelRate;
            claim.CapDays = claimUpdate.CapDays;
            claim.CapRate = claimUpdate.CapRate;
            claim.OtherAmount = claimUpdate.OtherAmount;
            claim.OtherDescription = claimUpdate.OtherDescription;
            claim.ShowPricingToStaff = claimUpdate.ShowPricingToStaff;
            claim.BankAccNo = claimUpdate.BankAccNo ?? string.Empty;
            claim.BankName = claimUpdate.BankName ?? string.Empty;
            claim.IfscCode = claimUpdate.IfscCode ?? string.Empty;
            claim.AdminRemarks = claimUpdate.AdminRemarks;
            if (!string.IsNullOrWhiteSpace(claimUpdate.Status)) claim.Status = claimUpdate.Status;
            if (claimUpdate.SubmittedAt.HasValue) claim.SubmittedAt = claimUpdate.SubmittedAt.Value;
            if (claimUpdate.ApprovedAt.HasValue) claim.ApprovedAt = claimUpdate.ApprovedAt.Value;
            if (!string.IsNullOrWhiteSpace(claimUpdate.ApprovedBy)) claim.ApprovedBy = claimUpdate.ApprovedBy;
            if (!string.IsNullOrWhiteSpace(claimUpdate.SavedBillNo)) claim.SavedBillNo = claimUpdate.SavedBillNo;
            claim.UpdatedAt = DateTime.UtcNow;

            var existingItems = claim.PaperItems.ToList();
            if (existingItems.Count > 0)
            {
                _context.StaffBillItems.RemoveRange(existingItems);
                claim.PaperItems.Clear();
            }

            decimal itemsTotal = 0m;
            foreach (var item in items)
            {
                var newItem = new StaffBillItem
                {
                    Id = Guid.NewGuid(),
                    ClaimId = claim.Id,
                    CollegeId = claim.CollegeId,
                    SubjectName = !string.IsNullOrWhiteSpace(item.SubjectName) ? item.SubjectName : (item.Subject ?? string.Empty),
                    Semester = !string.IsNullOrWhiteSpace(item.Semester) ? item.Semester : "Sem-3",
                    TheoryCount = item.TheoryCount,
                    TheoryRate = item.TheoryRate,
                    TwCount = item.TwCount,
                    TwRate = item.TwRate,
                    PracticalCount = item.PracticalCount,
                    PracticalRate = item.PracticalRate,
                    OralCount = item.OralCount,
                    OralRate = item.OralRate,
                    ProjectOralCount = item.ProjectOralCount,
                    ProjectOralRate = item.ProjectOralRate,
                    MeCount = item.MeCount,
                    MeRate = item.MeRate,
                    OwpCount = item.OwpCount,
                    OwpRate = item.OwpRate,
                    TtCount = item.TtCount,
                    TtRate = item.TtRate,
                };
                var th = newItem.TheoryCount * newItem.TheoryRate;
                var tw = newItem.TwCount * newItem.TwRate;
                var pr = newItem.PracticalCount * newItem.PracticalRate;
                var oral = newItem.OralCount * newItem.OralRate;
                var projOr = newItem.ProjectOralCount * newItem.ProjectOralRate;
                var me = newItem.MeCount * newItem.MeRate;
                var tt = newItem.TtCount * newItem.TtRate;
                newItem.ItemSubtotal = th + tw + pr + oral + projOr + me + tt;
                itemsTotal += newItem.ItemSubtotal;

                _context.StaffBillItems.Add(newItem);
            }

            decimal srTotal = claim.AttendedSeniorSupDays * claim.SeniorSupRate;
            decimal jrTotal = claim.AttendedJuniorSupDays * claim.JuniorSupRate;
            decimal travTotal = claim.IsExternal ? (claim.TravelDays * claim.TravelRate) : 0m;
            decimal capTotal = claim.CapDays * claim.CapRate;

            claim.GrandTotal = srTotal + jrTotal + travTotal + capTotal + itemsTotal + claim.OtherAmount;

            await _context.SaveChangesAsync();
            return claim;
        }

        public async Task<StaffBillingClaim> ApproveClaimAsync(Guid id, string approvedBy, string? appUrl = null)
        {
            return await ApproveClaimAsync(id.ToString(), approvedBy, appUrl);
        }

        public async Task<StaffBillingClaim> ApproveClaimAsync(string idOrToken, string approvedBy, string? appUrl = null)
        {
            StaffBillingClaim? claim = null;
            if (Guid.TryParse(idOrToken, out var guidId))
            {
                claim = await _context.StaffBillingClaims
                    .Include(c => c.PaperItems)
                    .FirstOrDefaultAsync(c => c.Id == guidId);
            }

            if (claim == null && !string.IsNullOrWhiteSpace(idOrToken))
            {
                claim = await _context.StaffBillingClaims
                    .Include(c => c.PaperItems)
                    .FirstOrDefaultAsync(c => c.Token == idOrToken.Trim());
            }

            if (claim == null && Guid.TryParse(idOrToken, out var staffGuid))
            {
                claim = await _context.StaffBillingClaims
                    .Include(c => c.PaperItems)
                    .FirstOrDefaultAsync(c => c.StaffId == staffGuid);
            }

            if (claim == null)
            {
                throw new KeyNotFoundException($"Claim not found for ID/Token: {idOrToken}");
            }

            if (string.IsNullOrWhiteSpace(claim.SavedBillNo))
            {
                var year = DateTime.UtcNow.Year;
                var isMod = (claim.RoleType ?? "").Equals("moderator", StringComparison.OrdinalIgnoreCase) || (claim.RoleType ?? "").Equals("external", StringComparison.OrdinalIgnoreCase) || claim.IsExternal;
                var prefix = isMod ? "MOD" : "STF";
                var count = await _context.StaffBillingClaims.CountAsync(c => c.Status == "APPROVED") + 1;
                claim.SavedBillNo = $"{prefix}/{year}/{count:D3}";
            }

            if (string.IsNullOrWhiteSpace(claim.Token))
            {
                claim.Token = "clm_" + Guid.NewGuid().ToString("N")[..12];
            }

            claim.Status = "APPROVED";
            claim.ApprovedAt = DateTime.UtcNow;
            claim.ApprovedBy = approvedBy;
            claim.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Best-effort confirmation: a mail outage must never undo the approval.
            if (!string.IsNullOrWhiteSpace(claim.Email))
            {
                try
                {
                    var baseUrl = BillingAppUrl(appUrl);
                    var claimLink = $"{baseUrl}/billing-claim?token={Uri.EscapeDataString(claim.Token)}";
                    var amount = claim.GrandTotal.ToString("N2");
                    var body = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 540px; margin: 0 auto; padding: 28px; border: 1px solid #e2e8f0; border-radius: 14px; background: #ffffff; color: #1e293b;"">
    <div style=""text-align: center; border-bottom: 2px solid #16a34a; padding-bottom: 16px; margin-bottom: 20px;"">
        <h2 style=""color: #166534; margin: 0; font-size: 20px;"">CLAIM APPROVED</h2>
        <p style=""color: #64748b; font-size: 13px; margin: 4px 0 0 0;"">Official Examination Remuneration</p>
    </div>
    
    <p style=""font-size: 14px;"">Dear <b>{System.Net.WebUtility.HtmlEncode(claim.StaffName)}</b>,</p>
    <p style=""font-size: 13.5px; color: #334155; line-height: 1.5;"">
        Your examination remuneration claim has been officially verified and approved by the Exam Office.
    </p>

    <div style=""background: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 10px; padding: 16px; margin: 18px 0;"">
        <table style=""width: 100%; font-size: 13px; border-collapse: collapse;"">
            <tr>
                <td style=""padding: 5px 0; color: #64748b;"">Approved Bill No.:</td>
                <td style=""padding: 5px 0; text-align: right; font-weight: 700; color: #166534;"">{System.Net.WebUtility.HtmlEncode(claim.SavedBillNo ?? "-")}</td>
            </tr>
            <tr>
                <td style=""padding: 5px 0; color: #64748b;"">Session:</td>
                <td style=""padding: 5px 0; text-align: right; font-weight: 600;"">{System.Net.WebUtility.HtmlEncode(claim.SessionName)}</td>
            </tr>
            <tr>
                <td style=""padding: 5px 0; color: #64748b;"">Approved Amount:</td>
                <td style=""padding: 5px 0; text-align: right; font-weight: 800; font-size: 16px; color: #15803d;"">₹ {amount}</td>
            </tr>
        </table>
    </div>

    <div style=""text-align: center; margin: 26px 0;"">
        <a href=""{claimLink}"" target=""_blank"" style=""background-color: #16a34a; color: #ffffff; padding: 13px 30px; font-size: 14px; font-weight: 700; text-decoration: none; border-radius: 8px; display: inline-block; box-shadow: 0 4px 6px -1px rgba(22,163,74,0.25);"">
            Download & Preview Approved Bill
        </a>
    </div>

    <p style=""font-size: 12.5px; color: #64748b; line-height: 1.5; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 12px; margin: 16px 0;"">
        <b>Security Verification:</b> When you open the link above, a 6-digit one-time verification code (OTP) will be sent to your registered email to verify your identity before accessing your official approved bill PDF.
    </p>

    <p style=""font-size: 12px; color: #94a3b8; word-break: break-all; margin-top: 20px;"">
        If the button does not open, copy and paste this link in your browser:<br/>
        <a href=""{claimLink}"" style=""color: #16a34a;"">{claimLink}</a>
    </p>

    <div style=""border-top: 1px solid #e2e8f0; margin-top: 24px; padding-top: 14px; font-size: 11.5px; color: #94a3b8; text-align: center;"">
        GradeSphere Examination Management System &bull; Confidential
    </div>
</div>";

                    await _emailService.SendEmailAsync(
                        claim.Email,
                        $"Examination Remuneration Claim Approved - {claim.SavedBillNo}",
                        body);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Billing: approval email failed for claim {ClaimId}", claim.Id);
                }
            }

            return claim;
        }

        public async Task<List<StaffBillingClaim>> SyncFromDutyChartAsync(Guid dutyChartId)
        {
            var chart = await _context.DutyCharts
                .Include(c => c.Entries)
                .FirstOrDefaultAsync(c => c.Id == dutyChartId);

            if (chart == null)
            {
                throw new KeyNotFoundException("Duty chart not found.");
            }

            var staffMasters = await _context.StaffMasters
                .Include(s => s.Department)
                .Include(s => s.Designation)
                .ToListAsync();
            var priceSets = await GetPriceSetsAsync();
            var activeSet = priceSets.FirstOrDefault(s => s.IsDefault) ?? priceSets[0];

            // Group entries by StaffId
            var staffEntries = chart.Entries
                .Where(e => e.StaffId != Guid.Empty)
                .GroupBy(e => e.StaffId)
                .ToList();

            var syncedClaims = new List<StaffBillingClaim>();

            foreach (var group in staffEntries)
            {
                var staffId = group.Key;
                var staff = staffMasters.FirstOrDefault(s => s.StaffId == staffId);
                var seniorCount = group.Count(e => e.Role == "Senior Supervisor");
                var juniorCount = group.Count(e => e.Role != "Senior Supervisor");

                var fullName = staff == null
                    ? "Faculty Member"
                    : string.Join(" ", new[] { staff.Title, staff.FirstName, staff.MiddleName, staff.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));

                var claim = await _context.StaffBillingClaims
                    .Include(c => c.PaperItems)
                    .FirstOrDefaultAsync(c => c.DutyChartId == dutyChartId && c.StaffId == staffId);

                if (claim == null)
                {
                    claim = new StaffBillingClaim
                    {
                        Id = Guid.NewGuid(),
                        DutyChartId = dutyChartId,
                        StaffId = staffId,
                        AYID = chart.AYID,
                        PriceSetId = activeSet.Id,
                        SessionName = chart.Name,
                        StaffName = fullName,
                        EmployeeCode = staff?.EmployeeCode ?? "",
                        Department = string.IsNullOrWhiteSpace(staff?.Department?.Name) ? "Administration" : staff!.Department!.Name,
                        Designation = string.IsNullOrWhiteSpace(staff?.Designation?.Name) ? "Assistant Professor" : staff!.Designation!.Name,
                        Email = staff?.Email ?? "",
                        RoleType = staff?.IsExternal == true ? "external" : "supervisor",
                        IsExternal = staff?.IsExternal ?? false,
                        VisitingCollegeName = staff?.ExternalCollege,
                        City = staff?.ExternalCity,
                        ShowPricingToStaff = activeSet.ShowPricingToStaff,
                        Token = "clm_" + Guid.NewGuid().ToString("N")[..12],
                        AssignedSeniorSupDays = seniorCount,
                        AttendedSeniorSupDays = seniorCount,
                        SeniorSupRate = activeSet.SeniorSupRate,
                        AssignedJuniorSupDays = juniorCount,
                        AttendedJuniorSupDays = juniorCount,
                        JuniorSupRate = activeSet.JuniorSupRate,
                        BankAccNo = "",
                        BankName = "",
                        IfscCode = "",
                        Status = "NOT_SENT",
                        CreatedAt = DateTime.UtcNow,
                    };
                    _context.StaffBillingClaims.Add(claim);
                }
                else
                {
                    claim.AssignedSeniorSupDays = seniorCount;
                    claim.AssignedJuniorSupDays = juniorCount;
                    if (claim.Status == "NOT_SENT")
                    {
                        claim.AttendedSeniorSupDays = seniorCount;
                        claim.AttendedJuniorSupDays = juniorCount;
                    }
                }

                syncedClaims.Add(claim);
            }

            await _context.SaveChangesAsync();
            return syncedClaims;
        }

        // ==========================================
        // EMAIL DISPATCH & OTP VERIFICATION
        // ==========================================

        public async Task<ClaimEmailResult> SendClaimEmailAsync(
            Guid? claimId,
            string? token,
            string appUrl,
            string? email = null,
            string? staffName = null,
            string? roleType = null,
            string? sessionName = null,
            Guid? staffId = null,
            Guid? dutyChartId = null,
            Guid? collegeId = null)
        {
            StaffBillingClaim? claim = null;

            if (claimId.HasValue && claimId.Value != Guid.Empty)
            {
                claim = await _context.StaffBillingClaims.FirstOrDefaultAsync(c => c.Id == claimId.Value);
            }

            if (claim == null && !string.IsNullOrWhiteSpace(token))
            {
                claim = await _context.StaffBillingClaims.FirstOrDefaultAsync(c => c.Token == token.Trim());
            }

            if (claim == null && staffId.HasValue && staffId.Value != Guid.Empty)
            {
                claim = await _context.StaffBillingClaims.FirstOrDefaultAsync(c => c.StaffId == staffId.Value);
            }

            // If not found in DB yet, create and persist automatically
            if (claim == null && !string.IsNullOrWhiteSpace(email))
            {
                claim = new StaffBillingClaim
                {
                    Id = claimId.HasValue && claimId.Value != Guid.Empty ? claimId.Value : Guid.NewGuid(),
                    Token = !string.IsNullOrWhiteSpace(token) ? token.Trim() : "clm_" + Guid.NewGuid().ToString("N")[..12],
                    StaffId = staffId,
                    DutyChartId = dutyChartId,
                    Email = email.Trim(),
                    StaffName = !string.IsNullOrWhiteSpace(staffName) ? staffName.Trim() : "Faculty Member",
                    RoleType = !string.IsNullOrWhiteSpace(roleType) ? roleType : "supervisor",
                    SessionName = !string.IsNullOrWhiteSpace(sessionName) ? sessionName : "Examination Session",
                    Status = "NOT_SENT",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                _context.StaffBillingClaims.Add(claim);
                await _context.SaveChangesAsync();
            }

            if (claim == null)
            {
                return new ClaimEmailResult
                {
                    Success = false,
                    Message = "Remuneration claim not found.",
                    FailedCount = 1,
                };
            }

            if (string.IsNullOrWhiteSpace(claim.Email) && !string.IsNullOrWhiteSpace(email))
            {
                claim.Email = email.Trim();
            }

            if (string.IsNullOrWhiteSpace(claim.Email))
            {
                return new ClaimEmailResult
                {
                    Success = false,
                    Message = $"No email address is registered for {claim.StaffName}.",
                    FailedCount = 1,
                };
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                claim.Token = token.Trim();
            }
            else if (string.IsNullOrWhiteSpace(claim.Token) 
                || string.Equals(claim.Status, "SUBMITTED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(claim.Status, "STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                claim.Token = "clm_" + Guid.NewGuid().ToString("N")[..12];
            }

            var baseUrl = BillingAppUrl(appUrl);
            // /billing-claim is the public staff site (the older /staff/claim route still opens too).
            var claimLink = $"{baseUrl}/billing-claim?token={Uri.EscapeDataString(claim.Token)}";

            var roleTitle = claim.RoleType == "moderator" ? "Paper Moderator" : (claim.RoleType == "external" ? "External Examiner" : "Examination Duties");
            var subject = $"Examination Remuneration Claim Form - {claim.StaffName}";
            var body = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 28px; border: 1px solid #e2e8f0; border-radius: 12px; background: #ffffff; color: #1e293b;"">
    <div style=""text-align: center; border-bottom: 2px solid #2563eb; padding-bottom: 16px; margin-bottom: 20px;"">
        <h2 style=""color: #1e3a8a; margin: 0; font-size: 20px;"">EXAMINATION SECTION</h2>
        <p style=""color: #64748b; font-size: 13px; margin: 4px 0 0 0;"">Official Remuneration & Duty Attendance Form</p>
    </div>
    
    <p style=""font-size: 15px; margin-bottom: 12px;"">Dear <b>{claim.StaffName}</b>,</p>
    <p style=""font-size: 14px; line-height: 1.5; color: #334155;"">
        Your examination remuneration claim link for <b>{(!string.IsNullOrWhiteSpace(claim.SessionName) ? claim.SessionName : roleTitle)}</b> is now ready for online submission.
    </p>

    <div style=""background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 16px; margin: 20px 0; font-size: 13px;"">
        <table style=""width: 100%; border-collapse: collapse;"">
            <tr>
                <td style=""color: #64748b; padding: 4px 0;"">Faculty Name:</td>
                <td style=""font-weight: 600; padding: 4px 0;"">{claim.StaffName}</td>
            </tr>
            <tr>
                <td style=""color: #64748b; padding: 4px 0;"">Designation / Role:</td>
                <td style=""font-weight: 600; padding: 4px 0;"">{claim.Designation ?? roleTitle}</td>
            </tr>
            <tr>
                <td style=""color: #64748b; padding: 4px 0;"">Department:</td>
                <td style=""font-weight: 600; padding: 4px 0;"">{claim.Department ?? "Administration"}</td>
            </tr>
        </table>
    </div>

    <div style=""text-align: center; margin: 30px 0;"">
        <a href=""{claimLink}"" target=""_blank"" style=""background-color: #2563eb; color: #ffffff; padding: 14px 32px; font-size: 15px; font-weight: 600; text-decoration: none; border-radius: 8px; display: inline-block; box-shadow: 0 4px 6px -1px rgba(37,99,235,0.2);"">
            Open Remuneration Claim Form
        </a>
    </div>

    <p style=""font-size: 13px; color: #64748b; line-height: 1.5;"">
        <b>Security Note:</b> When you open the form, a 6-digit one-time password (OTP) will be sent to your email to verify your identity before accessing your account information.
    </p>

    <p style=""font-size: 12px; color: #94a3b8; word-break: break-all; margin-top: 24px;"">
        If the button does not open, copy and paste this link in your browser:<br/>
        <a href=""{claimLink}"" style=""color: #2563eb;"">{claimLink}</a>
    </p>

    <div style=""border-top: 1px solid #e2e8f0; margin-top: 24px; padding-top: 16px; font-size: 12px; color: #94a3b8; text-align: center;"">
        GradeSphere Examination Management System &bull; Confidential &bull; Do not share this email.
    </div>
</div>
";

            try
            {
                // Same account rule as the report/student mail: the college's account, else the platform default.
                await _emailService.SendEmailAsync(claim.Email, subject, body);

                // An approved claim is permanently locked. Resending to a non-approved claim (even if submitted) resets status to LINK_SENT so faculty can re-open and update with fresh OTP.
                if (!string.Equals(claim.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
                {
                    claim.Status = "LINK_SENT";
                    claim.OtpVerifiedAt = null;
                }
                claim.SentAt = DateTime.UtcNow;
                claim.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return new ClaimEmailResult
                {
                    Success = true,
                    Message = $"Claim link successfully sent to {claim.Email}!",
                    SentCount = 1,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Billing: claim email to {Email} failed for claim {ClaimId}", claim.Email, claim.Id);
                return new ClaimEmailResult
                {
                    Success = false,
                    Message = $"Failed to send email to {claim.Email}: {ex.Message}",
                    FailedCount = 1,
                };
            }
        }

        public async Task<ClaimEmailResult> SendBatchClaimEmailsAsync(List<Guid> claimIds, string appUrl)
        {
            return await SendBatchClaimEmailsAsync(claimIds.Select(id => id.ToString()).ToList(), appUrl);
        }

        public async Task<ClaimEmailResult> SendBatchClaimEmailsAsync(List<string> claimIdsOrTokens, string appUrl)
        {
            int sent = 0;
            int failed = 0;
            var errors = new List<string>();

            foreach (var item in claimIdsOrTokens)
            {
                Guid? g = Guid.TryParse(item, out var parsed) ? parsed : null;
                string? tok = g == null ? item : null;
                var res = await SendClaimEmailAsync(g, tok, appUrl);
                if (res.Success) sent++;
                else
                {
                    failed++;
                    if (!string.IsNullOrWhiteSpace(res.Message)) errors.Add(res.Message);
                }
            }

            return new ClaimEmailResult
            {
                Success = sent > 0,
                SentCount = sent,
                FailedCount = failed,
                Message = sent > 0
                    ? $"Sent {sent} email{(sent == 1 ? "" : "s")} successfully.{(failed > 0 ? $" ({failed} failed)" : "")}"
                    : (errors.Count > 0 ? errors[0] : "No emails could be dispatched."),
            };
        }

        public async Task<ClaimPortalInfo> OpenClaimPortalAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new ClaimPortalInfo { Exists = false, Status = "INVALID" };
            }

            var claim = await _context.StaffBillingClaims
                .IgnoreQueryFilters()
                .Include(c => c.PaperItems)
                .FirstOrDefaultAsync(c => c.Token == token.Trim());

            if (claim == null)
            {
                return new ClaimPortalInfo { Exists = false, Status = "NOT_FOUND" };
            }

            if (string.Equals(claim.Status, "STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                return new ClaimPortalInfo
                {
                    Exists = true,
                    StaffName = claim.StaffName,
                    RoleType = claim.RoleType,
                    SessionName = claim.SessionName,
                    MaskedEmail = MaskEmail(claim.Email),
                    Status = "STOPPED",
                    Message = "This remuneration claim link has been stopped by the exam administrator. Please request a new link from the Exam Section.",
                    IsSubmitted = false,
                    IsApproved = false,
                    IsOtpVerified = false,
                    Claim = null,
                };
            }

            var isOtpVerified = IsOtpSessionValid(claim);
            var isApproved = string.Equals(claim.Status, "APPROVED", StringComparison.OrdinalIgnoreCase);
            var isSubmitted = string.Equals(claim.Status, "SUBMITTED", StringComparison.OrdinalIgnoreCase);

            return new ClaimPortalInfo
            {
                Exists = true,
                StaffName = claim.StaffName,
                RoleType = claim.RoleType,
                SessionName = claim.SessionName,
                MaskedEmail = MaskEmail(claim.Email),
                Status = claim.Status,
                IsSubmitted = isSubmitted || isApproved,
                IsApproved = isApproved,
                IsOtpVerified = isOtpVerified,
                Claim = (isOtpVerified || isSubmitted || isApproved) ? claim : null,
            };
        }

        public async Task<ClaimOtpResult> SendClaimOtpAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Invalid claim token.");
            }

            var claim = await _context.StaffBillingClaims
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Token == token.Trim());

            if (claim == null)
            {
                throw new KeyNotFoundException("Remuneration claim not found.");
            }

            if (string.Equals(claim.Status, "STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This remuneration claim link has been stopped by the exam administrator. Please request a new link.");
            }

            if (string.IsNullOrWhiteSpace(claim.Email))
            {
                throw new InvalidOperationException("No registered email address found for this faculty member.");
            }

            var now = DateTime.UtcNow;
            if (claim.OtpSentAt.HasValue && claim.OtpSentAt.Value > now.Subtract(OtpResendDelay))
            {
                var wait = Math.Max(1, (int)Math.Ceiling((claim.OtpSentAt.Value.Add(OtpResendDelay) - now).TotalSeconds));
                throw new InvalidOperationException($"A verification code was sent a moment ago. Please check your inbox (and spam), or wait {wait} seconds to request again.");
            }

            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var masked = MaskEmail(claim.Email);

            // Stored on the claim so it survives an app-pool recycle and works on any instance.
            claim.OtpCodeHash = HashOtp(code);
            claim.OtpExpiresAt = now.Add(OtpLifetime);
            claim.OtpSentAt = now;
            claim.OtpAttempts = 0;
            claim.UpdatedAt = now;
            await _context.SaveChangesAsync();

            var isApproved = string.Equals(claim.Status, "APPROVED", StringComparison.OrdinalIgnoreCase);
            var subject = isApproved
                ? $"Your Approved Bill Verification Code: {code}"
                : $"Your Examination Remuneration Verification Code: {code}";
            var headerTitle = isApproved ? "APPROVED REMUNERATION BILL" : "EXAM REMUNERATION PORTAL";
            var headerColor = isApproved ? "#166534" : "#1e3a8a";
            var borderColor = isApproved ? "#16a34a" : "#2563eb";
            var instruction = isApproved
                ? "Your 6-digit verification code to unlock, preview, and download your approved examination remuneration bill is:"
                : "Your 6-digit verification code to unlock and submit your examination remuneration claim is:";

            var body = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 500px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 12px; background: #ffffff; color: #1e293b;"">
    <div style=""text-align: center; border-bottom: 2px solid {borderColor}; padding-bottom: 12px; margin-bottom: 16px;"">
        <h2 style=""color: {headerColor}; margin: 0; font-size: 18px;"">{headerTitle}</h2>
        <p style=""color: #64748b; font-size: 12px; margin: 4px 0 0 0;"">Faculty One-Time Verification</p>
    </div>

    <p style=""font-size: 14px;"">Dear <b>{claim.StaffName}</b>,</p>
    <p style=""font-size: 13px; color: #475569; line-height: 1.5;"">
        {instruction}
    </p>

    <div style=""text-align: center; margin: 24px 0;"">
        <div style=""display: inline-block; background: #f0fdf4; border: 2px solid #86efac; border-radius: 10px; padding: 12px 28px;"">
            <span style=""font-size: 32px; font-weight: 800; letter-spacing: 8px; color: #15803d; font-family: monospace;"">{code}</span>
        </div>
    </div>

    <p style=""font-size: 12.5px; color: #64748b; text-align: center; margin-top: 16px;"">
        This code is valid for <b>10 minutes</b>. If you did not request this verification code, please ignore this email or notify the Exam Section.
    </p>
</div>
";

            try
            {
                await _emailService.SendEmailAsync(claim.Email, subject, body);
                return new ClaimOtpResult
                {
                    Success = true,
                    MaskedEmail = masked,
                    Message = $"Verification code sent to {masked}",
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unable to dispatch email: {ex.Message}");
            }
        }

        public async Task<VerifyClaimOtpResult> VerifyClaimOtpAsync(string token, string code)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Invalid claim token.");
            }

            var digits = new string((code ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length != 6)
            {
                throw new InvalidOperationException("Please enter the complete 6-digit code.");
            }

            var cleanToken = token.Trim();
            var claim = await _context.StaffBillingClaims
                .IgnoreQueryFilters()
                .Include(c => c.PaperItems)
                .FirstOrDefaultAsync(c => c.Token == cleanToken);

            if (claim == null)
            {
                throw new KeyNotFoundException("This remuneration claim link is not recognised. Please ask the Exam Office to send it again.");
            }

            if (string.Equals(claim.Status, "STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This remuneration claim link has been stopped by the exam administrator. Please request a new link.");
            }

            var now = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(claim.OtpCodeHash) || !claim.OtpExpiresAt.HasValue)
            {
                throw new InvalidOperationException("Verification code has expired or was not requested. Please request a new code.");
            }

            if (claim.OtpExpiresAt.Value <= now)
            {
                claim.OtpCodeHash = null;
                claim.OtpExpiresAt = null;
                claim.UpdatedAt = now;
                await _context.SaveChangesAsync();
                throw new InvalidOperationException("Verification code has expired. Please request a new code.");
            }

            if (claim.OtpAttempts >= MaxOtpAttempts)
            {
                throw new InvalidOperationException("Too many incorrect attempts. Please request a new verification code.");
            }

            if (!string.Equals(claim.OtpCodeHash, HashOtp(digits), StringComparison.OrdinalIgnoreCase))
            {
                claim.OtpAttempts += 1;
                claim.UpdatedAt = now;

                if (claim.OtpAttempts >= MaxOtpAttempts)
                {
                    claim.OtpCodeHash = null;
                    claim.OtpExpiresAt = null;
                    await _context.SaveChangesAsync();
                    throw new InvalidOperationException("Too many incorrect attempts. Please request a new verification code.");
                }

                await _context.SaveChangesAsync();
                var left = MaxOtpAttempts - claim.OtpAttempts;
                throw new InvalidOperationException($"That code is incorrect. {left} attempt{(left == 1 ? "" : "s")} remaining.");
            }

            // Success: burn the one-time code and unlock the claim for the session window.
            claim.OtpCodeHash = null;
            claim.OtpExpiresAt = null;
            claim.OtpAttempts = 0;
            claim.OtpVerifiedAt = now;
            claim.UpdatedAt = now;
            await _context.SaveChangesAsync();

            return new VerifyClaimOtpResult
            {
                Success = true,
                Verified = true,
                Message = "Verification successful! Access granted.",
                Claim = claim,
            };
        }

        public async Task<int> StopClaimsAsync(List<string>? tokens = null, List<Guid>? claimIds = null, Guid? dutyChartId = null)
        {
            var query = _context.StaffBillingClaims
                .IgnoreQueryFilters()
                .Where(c => c.Status != "APPROVED");

            if (claimIds != null && claimIds.Count > 0)
            {
                query = query.Where(c => claimIds.Contains(c.Id));
            }
            else if (tokens != null && tokens.Count > 0)
            {
                var cleanTokens = tokens.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
                query = query.Where(c => cleanTokens.Contains(c.Token));
            }
            else if (dutyChartId.HasValue && dutyChartId.Value != Guid.Empty)
            {
                query = query.Where(c => c.DutyChartId == dutyChartId.Value);
            }
            else
            {
                return 0;
            }

            var claims = await query.ToListAsync();
            if (claims.Count == 0) return 0;

            var now = DateTime.UtcNow;
            foreach (var claim in claims)
            {
                claim.Status = "STOPPED";
                claim.OtpVerifiedAt = null;
                claim.OtpCodeHash = null;
                claim.OtpExpiresAt = null;
                claim.UpdatedAt = now;
            }

            await _context.SaveChangesAsync();
            return claims.Count;
        }
    }
}
