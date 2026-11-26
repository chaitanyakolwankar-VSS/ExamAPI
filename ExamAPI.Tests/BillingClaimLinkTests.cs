using System.Text.RegularExpressions;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Billing;
using ExamAPI.Services.Email;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// The emailed billing claim link (anonymous): the link alone shows no details and cannot submit; after the emailed code
/// the staff member submits counts and bank details, while the rates stay the college's own; a college stops only its
/// own claims.
/// </summary>
public sealed class BillingClaimLinkTests
{
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly FakeEmail _email = new();

    private sealed class FakeEmail : IEmailService
    {
        public List<string> Subjects { get; } = new();
        public Task SendEmailAsync(string toEmail, string subject, string body, Guid? collegeId = null)
        {
            Subjects.Add(subject);
            return Task.CompletedTask;
        }
    }

    /// <summary>A context as a college user, or anonymous (null) like the claim pages.</summary>
    private ApplicationDbContext Context(Guid? college)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.CollegeId).Returns(college);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_db).Options;
        return new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, user.Object);
    }

    private BillingService Service(ApplicationDbContext context) => new(context, _email, NullLogger<BillingService>.Instance);

    /// <summary>A sent claim of the college, its rate set, and another college's set with tempting rates.</summary>
    private StaffBillingClaim SentClaim(Guid? college = null)
    {
        using var context = Context(null);
        var set = new BillingPriceSet { CollegeId = _college, Name = "Ours", IsDefault = true, TheoryAssessmentRate = 16m, ModerationRate = 20m, SeniorSupRate = 250m, JuniorSupRate = 150m };
        var other = new BillingPriceSet { CollegeId = _otherCollege, Name = "Theirs", IsDefault = true, TheoryAssessmentRate = 99m, SeniorSupRate = 9999m, JuniorSupRate = 150m };
        if (!context.BillingPriceSets.IgnoreQueryFilters().Any()) context.BillingPriceSets.AddRange(set, other);
        var claim = new StaffBillingClaim
        {
            CollegeId = college ?? _college, Token = "clm_" + Guid.NewGuid().ToString("N"), StaffName = "Prof. Asha Patil", Email = "asha@demo.invalid",
            RoleType = "supervisor", Status = "LINK_SENT", SentAt = DateTime.UtcNow, SeniorSupRate = 250m, JuniorSupRate = 150m,
            AssignedJuniorSupDays = 3
        };
        context.StaffBillingClaims.Add(claim);
        context.SaveChanges();
        return claim;
    }

    private static StaffBillingClaim Form(int juniorDays, decimal fakeRate) => new()
    {
        AttendedJuniorSupDays = juniorDays, JuniorSupRate = fakeRate, SeniorSupRate = fakeRate, TravelRate = fakeRate, CapRate = fakeRate,
        BankAccNo = "123456789", BankName = "Demo Bank", IfscCode = "DEMO0001"
    };

    private static List<StaffBillItem> Papers(int count, decimal fakeRate) =>
        new() { new StaffBillItem { SubjectName = "Maths", TheoryCount = count, TheoryRate = fakeRate } };

    private async Task VerifyAsync(string token)
    {
        using var context = Context(null);
        var service = Service(context);
        await service.SendClaimOtpAsync(token);
        var code = Regex.Match(_email.Subjects[^1], @"\d{6}").Value;
        await service.VerifyClaimOtpAsync(token, code);
    }

    [Fact]
    public async Task The_link_alone_shows_no_details_and_cannot_submit()
    {
        var claim = SentClaim();
        using var context = Context(null);
        var service = Service(context);

        var portal = await service.OpenClaimPortalAsync(claim.Token);
        Assert.True(portal.Exists);
        Assert.Null(portal.Claim);
        Assert.Null(await service.GetClaimByTokenAsync(claim.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SubmitClaimAsync(claim.Token, Form(3, 150m), Papers(10, 16m)));

        using var check = Context(null);
        Assert.Equal("LINK_SENT", check.StaffBillingClaims.IgnoreQueryFilters().Single(c => c.Id == claim.Id).Status);
    }

    [Fact]
    public async Task After_the_code_the_claim_is_submitted_with_the_colleges_rates_not_the_forms()
    {
        var claim = SentClaim();
        await VerifyAsync(claim.Token);

        using (var context = Context(null))
        {
            var saved = await Service(context).SubmitClaimAsync(claim.Token, Form(3, 9999m), Papers(10, 9999m));
            Assert.Equal("SUBMITTED", saved.Status);
            Assert.Equal(3 * 150m + 10 * 16m, saved.GrandTotal);   // the claim's own day rate and its college's paper rate
            Assert.Equal(16m, saved.PaperItems.Single().TheoryRate);
            Assert.Equal("Ours", saved.PriceSetName);
        }

        // Submitted: a forwarded link still shows nothing without the code
        using (var context = Context(null))
        {
            var row = context.StaffBillingClaims.IgnoreQueryFilters().Single(c => c.Id == claim.Id);
            row.OtpVerifiedAt = null;
            context.SaveChanges();
            var portal = await Service(context).OpenClaimPortalAsync(claim.Token);
            Assert.True(portal.IsSubmitted);
            Assert.Null(portal.Claim);
        }
    }

    [Fact]
    public async Task Negative_days_or_counts_are_refused()
    {
        var claim = SentClaim();
        await VerifyAsync(claim.Token);
        using var context = Context(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context).SubmitClaimAsync(claim.Token, Form(-5, 150m), Papers(10, 16m)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context).SubmitClaimAsync(claim.Token, Form(3, 150m), Papers(-10, 16m)));
    }

    [Fact]
    public async Task A_college_stops_only_its_own_claims()
    {
        var theirs = SentClaim(_otherCollege);
        using (var context = Context(_college))
        {
            Assert.Equal(0, await Service(context).StopClaimsAsync(claimIds: new List<Guid> { theirs.Id }));
        }
        using var check = Context(null);
        Assert.Equal("LINK_SENT", check.StaffBillingClaims.IgnoreQueryFilters().Single(c => c.Id == theirs.Id).Status);
    }
}
