using System.Security.Claims;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using ExamAPI.Services.Files;
using ExamAPI.Services.PasswordResetOTP;
using ExamAPI.Services.StudentAccess;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>
/// Student access, step 6: the student changes the fields their link allows (others are ignored), checked like
/// the staff form, audited with old and new values, the link shows Updated with what changed, and the student
/// gets one notice email per 10 minutes. Also the sign-in page's "Forgot password" by email.
/// </summary>
public sealed class StudentDetailsTests : IDisposable
{
    private const string ServerKey = "test-server-key-that-is-at-least-32-chars";
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly string _base = Path.Combine(Path.GetTempPath(), "student-details-" + Guid.NewGuid().ToString("N"));
    private readonly FileStorage _storage;
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly Guid _link = Guid.NewGuid();
    private readonly Guid _batch = Guid.NewGuid();
    private static readonly string PngDataUrl = "data:image/png;base64," + Convert.ToBase64String(UploadsTests.Png);

    private ApplicationDbContext Context(Guid? college)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_db).Options;
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.CollegeId).Returns(college);
        return new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, user.Object);
    }

    public StudentDetailsTests()
    {
        Directory.CreateDirectory(_base);
        _storage = new FileStorage(_base, null, Path.Combine(_base, "store"));
        using var context = Context(_college);
        context.Database.EnsureCreated();
        context.Colleges.Add(new College { CollegeId = _college, Name = "VIVA", CollegeCode = "VIVA", CollegeCenter = "Virar", ContactEmail = "a@b.in", ContactPhone = "1" });
        context.StudentMasters.AddRange(
            new StudentMaster { StdMstId = _student, StudentId = "2400001", FirstName = "Asha", LastName = "Patil", MotherName = "Sunita", Gender = "Female", Category = "OPEN", Email = "asha@viva.in", CollegeId = _college },
            new StudentMaster { StdMstId = _other, StudentId = "2400002", FirstName = "Ravi", LastName = "Patil", StudentPRN = "ERN-77", CollegeId = _college });
        context.TaskBatches.Add(new TaskBatch { Id = _batch, CollegeId = _college, TaskTypeCode = TaskTypes.StudentMaster, Title = "t", Deadline = DateTime.UtcNow.AddDays(3), EditableFields = "phone,address,prn,photo,dateOfBirth" });
        context.TaskLinks.Add(new TaskLink { Id = _link, CollegeId = _college, TaskTypeCode = TaskTypes.StudentMaster, BatchId = _batch, StdMstId = _student, ExpiresAt = DateTime.UtcNow.AddDays(3) });
        context.SaveChanges();
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    private ClaimsPrincipal Student() => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, _student.ToString()),
        new Claim(ClaimTypes.Role, StudentAccessAuth.RoleName),
        new Claim("CollegeId", _college.ToString()),
        new Claim(StudentAccessAuth.LinkClaim, _link.ToString()),
        new Claim(StudentAccessAuth.SessionClaim, Guid.NewGuid().ToString()),
        new Claim(StudentAccessAuth.TaskClaim, TaskTypes.StudentMaster),
    }, StudentAccessAuth.Scheme));

    private StudentDetailsService Service(ApplicationDbContext context)
    {
        var resolver = new EmailAccountResolver(context, new SecretProtector(ServerKey));
        var outbox = new EmailOutbox(context, resolver, new Mock<IEmailSender>().Object, NullLogger<EmailOutbox>.Instance);
        return new StudentDetailsService(context, new StudentSignInService(context, new StudentAccessSecrets(ServerKey), outbox), outbox, _storage);
    }

    [Fact]
    public async Task Only_allowed_fields_change_and_the_change_is_audited_and_noticed()
    {
        using var context = Context(_college);
        var result = await Service(context).SaveAsync(Student(), new StudentDetailsUpdate
        {
            FirstName = "Hacked",              // not allowed by this batch: ignored
            Phone = "+91 98765 43210",
            Address = "  12, MG Road,   Virar ",
            DateOfBirth = new DateTime(2005, 3, 14),
        });

        Assert.Equal(new[] { "Date of birth", "Phone", "Address" }, result.Changed);
        var s = await context.StudentMasters.SingleAsync(x => x.StdMstId == _student);
        Assert.Equal(("Asha", "9876543210", "12, MG Road, Virar"), (s.FirstName, s.Phone, s.Address));
        Assert.Equal(new DateTime(2005, 3, 14), s.DateOfBirth);
        Assert.NotNull((await context.TaskLinks.SingleAsync()).UsedAt);
        Assert.NotNull(result.Home.UpdatedAt);

        var audit = await context.AuditLogs.SingleAsync(a => a.Event == StudentDetailsService.ChangedEvent);
        Assert.Equal((_link.ToString(), (Guid?)_student, "Student"), (audit.RecordId, audit.PerformedBy, audit.UserType));
        Assert.Contains("Phone: '' -> '9876543210'", audit.Details);

        var notice = await context.EmailQueue.SingleAsync(m => m.Kind == EmailKinds.Plain);
        Assert.Equal(("asha@viva.in", "VIVA: your details were updated"), (notice.ToAddress, notice.Subject));
        Assert.Contains("- Date of birth\n- Phone\n- Address", notice.TextBody);
        Assert.Contains("Open the same link again, correct it and save", notice.TextBody);
    }

    [Fact]
    public async Task Saving_the_same_values_changes_nothing_and_each_save_that_changes_something_sends_its_own_notice()
    {
        using var context = Context(_college);
        await Service(context).SaveAsync(Student(), new StudentDetailsUpdate { Phone = "9876543210" });
        var same = await Service(context).SaveAsync(Student(), new StudentDetailsUpdate { Phone = "9876543210" });
        Assert.Empty(same.Changed);
        var again = await Service(context).SaveAsync(Student(), new StudentDetailsUpdate { Phone = "9123456789" });
        Assert.Equal(new[] { "Phone" }, again.Changed);
        Assert.Equal(2, await context.EmailQueue.CountAsync(m => m.Kind == EmailKinds.Plain));
    }

    [Fact]
    public async Task A_photo_saved_with_other_details_is_one_save_with_one_notice()
    {
        using var context = Context(_college);
        var result = await Service(context).SaveAsync(Student(), new StudentDetailsUpdate { Phone = "9876543210", Photo = PngDataUrl });
        Assert.Equal(new[] { "Phone", "Photo" }, result.Changed);
        Assert.True(result.Home.Student.HasPhoto);
        Assert.Equal(1, await context.EmailQueue.CountAsync(m => m.Kind == EmailKinds.Plain));
        Assert.Equal(1, await context.AuditLogs.CountAsync(a => a.Event == StudentDetailsService.ChangedEvent));

        // A bad phone stops the whole save: the picture isn't stored either.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context).SaveAsync(Student(), new StudentDetailsUpdate { Phone = "123", Photo = PngDataUrl }));
        Assert.Equal(1, await context.AuditLogs.CountAsync(a => a.Event == StudentDetailsService.ChangedEvent));
    }

    [Theory]
    [InlineData("12345", null, null, "Phone: enter a 10-digit mobile number.")]
    [InlineData(null, "ERN-77", null, "This ERN / PRN belongs to another student.")]
    [InlineData(null, "ERN 77!", null, "ERN / PRN: letters, digits")]
    [InlineData(null, null, "2020-01-01", "Date of birth: check the year.")]
    public async Task Bad_values_are_refused_with_a_plain_sentence(string? phone, string? prn, string? dob, string message)
    {
        using var context = Context(_college);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context).SaveAsync(Student(),
            new StudentDetailsUpdate { Phone = phone, Prn = prn, DateOfBirth = dob == null ? null : DateTime.Parse(dob) }));
        Assert.StartsWith(message, ex.Message);
        Assert.Null((await context.TaskLinks.SingleAsync()).UsedAt);
    }

    [Fact]
    public async Task A_photo_is_stored_and_read_back_and_a_locked_signature_is_refused()
    {
        using var context = Context(_college);
        var result = await Service(context).SaveImageAsync(Student(), StudentImageKinds.Photo, PngDataUrl);
        Assert.Equal(new[] { "Photo" }, result.Changed);
        Assert.True(result.Home.Student.HasPhoto);
        var image = await Service(context).ReadImageAsync(Student(), StudentImageKinds.Photo);
        Assert.Equal(UploadsTests.Png, image!.Value.Bytes);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context).SaveImageAsync(Student(), StudentImageKinds.Signature, PngDataUrl));
        Assert.Contains("can't be changed", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context).SaveImageAsync(Student(), StudentImageKinds.Photo, "data:image/png;base64,bm90IGFuIGltYWdl"));
    }

    [Fact]
    public async Task A_closed_link_cannot_save()
    {
        using (var context = Context(_college))
        {
            (await context.TaskLinks.SingleAsync()).Status = TaskLinkStatuses.Revoked;
            await context.SaveChangesAsync();
        }
        using var c = Context(_college);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(c).SaveAsync(Student(), new StudentDetailsUpdate { Phone = "9876543210" }));
    }

    [Fact]
    public async Task Staff_see_what_the_student_changed()
    {
        using (var context = Context(_college))
            await Service(context).SaveAsync(Student(), new StudentDetailsUpdate { Phone = "9876543210" });
        using var staff = Context(_college);
        var resolver = new EmailAccountResolver(staff, new SecretProtector(ServerKey));
        var outbox = new EmailOutbox(staff, resolver, new Mock<IEmailSender>().Object, NullLogger<EmailOutbox>.Instance);
        var detail = await new TaskLinkService(staff, outbox, resolver, new ExamAPI.Services.GenerateHallTicket.GenerateHallTicketService(staff)).GetBatchAsync(_batch);
        var row = Assert.Single(detail.Links);
        Assert.Equal(LinkStates.Updated, row.State);
        Assert.Contains("Phone", row.Changes);
    }

    // ------------------------------------------------------------------ forgot password by email

    private sealed class FakeEmail : IEmailService
    {
        public List<(string To, string Body)> Sent { get; } = new();
        public Task SendEmailAsync(string toEmail, string subject, string body)
        {
            Sent.Add((toEmail, body));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Forgot_password_by_email_sends_a_code_only_to_a_real_account_and_the_code_sets_the_password()
    {
        using var context = Context(null);
        var userId = Guid.NewGuid();
        context.UserMasters.Add(new UserMaster { UserId = userId, Username = "t", FirstName = "T", LastName = "U", Email = "Staff@Viva.in", HashedPassword = "x", CollegeId = _college });
        await context.SaveChangesAsync();
        var mail = new FakeEmail();
        var service = new PasswordResetService(context, mail);

        await service.SendResetOtpByEmailAsync("nobody@viva.in"); // no error, nothing sent
        Assert.Empty(mail.Sent);
        await service.SendResetOtpByEmailAsync(" staff@viva.in ");
        Assert.Equal("Staff@Viva.in", Assert.Single(mail.Sent).To);
        var code = (await context.PasswordResetOTPs.SingleAsync()).OTP;

        Assert.Contains("8 characters", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResetPasswordByEmailAsync("staff@viva.in", code, "short"))).Message);
        Assert.Contains("4 tries left", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResetPasswordByEmailAsync("staff@viva.in", code == "000000" ? "111111" : "000000", "long-enough-1"))).Message);
        await service.ResetPasswordByEmailAsync("staff@viva.in", code, "long-enough-1");

        var user = await context.UserMasters.IgnoreQueryFilters().SingleAsync();
        Assert.True(BCrypt.Net.BCrypt.Verify("long-enough-1", user.HashedPassword));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResetPasswordByEmailAsync("staff@viva.in", code, "long-enough-2")); // used
    }
}
