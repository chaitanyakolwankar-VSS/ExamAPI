using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.StudentAccess;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>Student access, step 3: staff see missing / flagged emails, correct them, mark them verified; all audited.</summary>
public sealed class StudentEmailReviewTests
{
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly Guid _civil = Guid.NewGuid();
    private readonly Guid _mech = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly StudentEmailReviewService _service;

    private ApplicationDbContext Context(Guid? college)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_db).Options;
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.CollegeId).Returns(college);
        user.SetupGet(u => u.UserId).Returns(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        return new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, user.Object);
    }

    public StudentEmailReviewTests()
    {
        _context = Context(_college);
        _context.CourseMasters.AddRange(
            new CourseMaster { CourseId = _civil, Name = "Civil", CourseCode = "CIV", CollegeId = _college },
            new CourseMaster { CourseId = _mech, Name = "Mechanical", CourseCode = "MEC", CollegeId = _college });
        Student("2600001", "Asha", _civil, null);
        Student("2600002", "Ravi", _civil, "ravi@gmail.com", EmailVerification.Flagged, "Domain gmail.com is not one of the college's (viva.in).");
        Student("2600003", "Mina", _civil, "mina@viva.in");
        Student("2600004", "Gita", _mech, "gita@viva.in", EmailVerification.Verified);
        _context.SaveChanges();
        using (var other = Context(_other))
        {
            other.StudentMasters.Add(new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "2600009", FirstName = "Else", LastName = "Where", CollegeId = _other });
            other.StudentEligibilities.Add(new StudentEligibility { Id = Guid.NewGuid(), StudentId = "2600009", CourseId = _civil, AYID = _ay, SemesterId = "Sem-1", Pattern = "NEP", CollegeId = _other });
            other.SaveChanges();
        }
        _service = new StudentEmailReviewService(_context);
    }

    private void Student(string id, string first, Guid course, string? email, string? status = null, string? reason = null)
    {
        var s = new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = id, FirstName = first, LastName = "Patil", Email = email, EmailStatus = status, EmailFlagReason = reason, CollegeId = _college };
        _context.StudentMasters.Add(s);
        _context.StudentEligibilities.Add(new StudentEligibility { Id = Guid.NewGuid(), StdMstId = s.StdMstId, StudentId = id, CourseId = course, AYID = _ay, SemesterId = "Sem-1", Pattern = "NEP", CollegeId = _college });
    }

    [Fact]
    public async Task By_default_only_missing_and_flagged_are_listed_with_counts_for_the_whole_college()
    {
        var list = await _service.ListAsync(new StudentEmailFilter { AYID = _ay });

        Assert.Equal((1, 1, 1, 1), (list.Missing, list.Flagged, list.Unchecked, list.Verified));
        Assert.Equal(new[] { "2600001", "2600002" }, list.Rows.Select(r => r.StudentId));
        Assert.Equal("No email yet.", list.Rows[0].Reason);
        Assert.Contains("gmail.com", list.Rows[1].Reason);
        Assert.DoesNotContain(list.Rows, r => r.StudentId == "2600009"); // another college
    }

    [Fact]
    public async Task A_branch_and_the_show_filter_narrow_the_list()
    {
        var mech = await _service.ListAsync(new StudentEmailFilter { AYID = _ay, CourseId = _mech, Show = "all" });
        Assert.Equal("2600004", Assert.Single(mech.Rows).StudentId);
        Assert.Equal(StudentEmailStates.Verified, mech.Rows[0].Status);

        var unchecked_ = await _service.ListAsync(new StudentEmailFilter { AYID = _ay, Show = "unchecked" });
        Assert.Equal("2600003", Assert.Single(unchecked_.Rows).StudentId);
    }

    [Fact]
    public async Task Staff_can_fill_in_a_missing_email_and_verify_it_in_one_step_and_it_is_audited()
    {
        var row = await _service.UpdateAsync("2600001", new StudentEmailUpdate { Email = " Asha@Viva.in ", Verify = true });

        Assert.Equal(("asha@viva.in", StudentEmailStates.Verified), (row.Email, row.Status));
        var events = await _context.AuditLogs.Where(a => a.Event != null).OrderBy(a => a.Event).Select(a => a.Event).ToListAsync();
        Assert.Equal(new[] { "EmailSet", "EmailVerified" }, events);
        Assert.All(await _context.AuditLogs.Where(a => a.Event != null).ToListAsync(), a =>
        {
            Assert.Equal(_college, a.CollegeId);
            Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), a.PerformedBy);
        });
    }

    [Fact]
    public async Task Verifying_a_flagged_email_overrides_the_flag_and_records_why_it_was_flagged()
    {
        var row = await _service.UpdateAsync("2600002", new StudentEmailUpdate { Email = "ravi@gmail.com", Verify = true });

        Assert.Equal(StudentEmailStates.Verified, row.Status);
        Assert.Null((await _context.StudentMasters.SingleAsync(s => s.StudentId == "2600002")).EmailFlagReason);
        Assert.Contains("was flagged", (await _context.AuditLogs.SingleAsync(a => a.Event == "EmailVerified")).Details);
    }

    [Fact]
    public async Task A_bad_or_taken_email_is_refused_and_changes_nothing()
    {
        Assert.Contains("not a valid", (await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync("2600001", new StudentEmailUpdate { Email = "x@y" }))).Message);
        Assert.Contains("already belongs to student 2600003", (await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync("2600001", new StudentEmailUpdate { Email = "MINA@viva.in" }))).Message);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateAsync("2600009", new StudentEmailUpdate { Email = "a@b.in" })); // other college
        Assert.Null((await _context.StudentMasters.SingleAsync(s => s.StudentId == "2600001")).Email);
        Assert.Empty(await _context.AuditLogs.Where(a => a.Event != null).ToListAsync());
    }

    [Fact]
    public async Task Bulk_verify_skips_students_without_an_email_and_ones_already_verified()
    {
        var count = await _service.VerifyAsync(new[] { "2600001", "2600002", "2600003", "2600004", "2600009" });

        Assert.Equal(2, count);
        var list = await _service.ListAsync(new StudentEmailFilter { AYID = _ay, Show = "all" });
        Assert.Equal((1, 0, 0, 3), (list.Missing, list.Flagged, list.Unchecked, list.Verified));
    }

    [Fact]
    public async Task New_domain_settings_recheck_unverified_emails_but_leave_verified_ones()
    {
        var flagged = await _service.SaveSettingsAsync(new EmailSettingsDto { AllowedEmailDomains = "@Gmail.com", ExamOfficeEmail = " Exams@Viva.in ", DailyEmailLimit = 500 });

        Assert.Equal(1, flagged); // mina@viva.in is now outside the list; ravi@gmail.com is inside it
        var students = await _context.StudentMasters.ToDictionaryAsync(s => s.StudentId);
        Assert.Null(students["2600002"].EmailStatus);
        Assert.Equal(EmailVerification.Flagged, students["2600003"].EmailStatus);
        Assert.Equal(EmailVerification.Verified, students["2600004"].EmailStatus); // gita@viva.in stays verified
        var settings = await _service.GetSettingsAsync();
        Assert.Equal(("gmail.com", "exams@viva.in", 500), (settings.AllowedEmailDomains, settings.ExamOfficeEmail, settings.DailyEmailLimit));
        Assert.Equal(_college, (await _context.CollegeSettings.SingleAsync()).CollegeId);
        Assert.Single(await _context.AuditLogs.Where(a => a.Event == "EmailSettingsChanged").ToListAsync());
    }

    [Fact]
    public async Task Bad_settings_are_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSettingsAsync(new EmailSettingsDto { AllowedEmailDomains = "not a domain" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSettingsAsync(new EmailSettingsDto { ExamOfficeEmail = "nope" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSettingsAsync(new EmailSettingsDto { DailyEmailLimit = 0 }));
        Assert.Empty(await _context.CollegeSettings.ToListAsync());
    }
}
