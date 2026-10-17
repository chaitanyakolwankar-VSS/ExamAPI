using System.Security.Cryptography;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>
/// Student access, step 4: mail accounts set by the platform admin (encrypted password, college account else the
/// platform default), the outbox (OTP now, bulk queued) and the background sender (priority, daily limit, retries).
/// </summary>
public sealed class EmailSendingTests
{
    private const string ServerKey = "test-server-key-that-is-at-least-32-chars";
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly SecretProtector _protector = new(ServerKey);
    private readonly FakeSender _sender = new();

    private sealed class FakeSender : IEmailSender
    {
        public List<(EmailAccountSettings Account, EmailMessage Message)> Sent { get; } = new();
        public Exception? FailWith { get; set; }

        public Task SendAsync(EmailAccountSettings account, EmailMessage message, CancellationToken ct = default)
        {
            if (FailWith != null) throw FailWith;
            Sent.Add((account, message));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRenderer : IEmailBodyRenderer
    {
        public string Kind => EmailKinds.TaskLink;
        public bool Revoked { get; set; }
        public Task<EmailMessage?> RenderAsync(EmailQueue item, string? examOfficeEmail, CancellationToken ct) =>
            Task.FromResult(Revoked ? null : EmailLayout.Build(item.ToAddress, "Your link", "<p>link</p>", "link", examOfficeEmail));
    }

    /// <summary>A context as a signed-in user of <paramref name="college"/> (null = platform admin or the background sender).</summary>
    private ApplicationDbContext Context(Guid? college = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_db).Options;
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.CollegeId).Returns(college);
        return new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, user.Object);
    }

    public EmailSendingTests()
    {
        using var context = Context();
        context.Colleges.AddRange(
            new College { CollegeId = _college, Name = "VIVA", CollegeCode = "VIVA", CollegeCenter = "Virar", ContactEmail = "a@b.in", ContactPhone = "1" },
            new College { CollegeId = _other, Name = "Other", CollegeCode = "OTH", CollegeCenter = "X", ContactEmail = "a@b.in", ContactPhone = "1" });
        context.SaveChanges();
    }

    private EmailAccountService Accounts(ApplicationDbContext context) =>
        new(context, _protector, new EmailAccountResolver(context, _protector), _sender);

    private static EmailAccountInput Gmail(string user, string? password = "abcd efgh ijkl mnop") => new()
    {
        Host = "smtp.gmail.com", Port = 587, UseStartTls = true, Username = user, Password = password, FromAddress = user, FromName = "Exam Office",
    };

    private EmailQueueProcessor Processor(ApplicationDbContext context, FakeRenderer? renderer = null) =>
        new(context, new EmailAccountResolver(context, _protector), _sender, renderer == null ? Array.Empty<IEmailBodyRenderer>() : new[] { renderer }, NullLogger<EmailQueueProcessor>.Instance);

    // ------------------------------------------------------------------ password protection

    [Fact]
    public void A_password_round_trips_but_a_changed_value_or_another_server_key_cannot_read_it()
    {
        var protectedValue = _protector.Protect("app-password");

        Assert.StartsWith("v1.", protectedValue);
        Assert.DoesNotContain("app-password", protectedValue);
        Assert.Equal("app-password", _protector.Unprotect(protectedValue));
        Assert.NotEqual(protectedValue, _protector.Protect("app-password")); // fresh nonce every time

        var bytes = Convert.FromBase64String(protectedValue[3..]);
        bytes[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect("v1." + Convert.ToBase64String(bytes)));
        Assert.ThrowsAny<CryptographicException>(() => new SecretProtector(ServerKey + "x").Unprotect(protectedValue));
    }

    // ------------------------------------------------------------------ accounts (platform admin)

    [Fact]
    public async Task A_college_uses_its_own_account_else_the_platform_default_else_none()
    {
        using var context = Context();
        var resolver = new EmailAccountResolver(context, _protector);
        Assert.Equal(EmailAccountSources.None, (await resolver.ForCollegeAsync(_college)).Source);

        await Accounts(context).SaveAsync(null, Gmail("platform@gmail.com"));
        var (fallback, source) = await resolver.ForCollegeAsync(_college);
        Assert.Equal((EmailAccountSources.Default, "platform@gmail.com"), (source, fallback!.Username));
        Assert.Equal("abcdefghijklmnop", fallback.Password); // spaces of Google's display removed, decrypted for sending

        await Accounts(context).SaveAsync(_college, Gmail("viva@gmail.com"));
        Assert.Equal("viva@gmail.com", (await resolver.ForCollegeAsync(_college)).Account!.Username);
        Assert.Equal("platform@gmail.com", (await resolver.ForCollegeAsync(_other)).Account!.Username);
    }

    [Fact]
    public async Task The_password_is_stored_encrypted_never_shown_and_kept_when_left_blank()
    {
        using var context = Context();
        var service = Accounts(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(_college, Gmail("viva@gmail.com", password: "")));
        var view = await service.SaveAsync(_college, Gmail("viva@gmail.com"));

        Assert.True(view.HasPassword);
        Assert.DoesNotContain("abcd", (await context.EmailAccounts.SingleAsync()).PasswordProtected);
        Assert.Null(typeof(EmailAccountView).GetProperty("Password"));

        var edit = Gmail("viva@gmail.com", password: " ");
        edit.FromName = "VIVA Exams";
        await service.SaveAsync(_college, edit);
        var (account, _) = await new EmailAccountResolver(context, _protector).ForCollegeAsync(_college);
        Assert.Equal(("abcdefghijklmnop", "VIVA Exams"), (account!.Password, account.FromName));

        var audit = await context.AuditLogs.IgnoreQueryFilters().Where(a => a.TableName == nameof(EmailAccount)).OrderBy(a => a.Timestamp).ToListAsync();
        Assert.Equal(new[] { "EmailAccountAdded", "EmailAccountChanged" }, audit.Select(a => a.Event));
        Assert.All(audit, a => Assert.DoesNotContain("abcd", a.Details));
        Assert.Contains("password kept", audit[1].Details);
        Assert.All(audit, a => Assert.Equal(_college, a.CollegeId));
    }

    [Fact]
    public async Task Bad_account_details_or_an_unknown_college_are_refused()
    {
        using var context = Context();
        var service = Accounts(context);
        var noHost = Gmail("a@gmail.com");
        noHost.Host = "smtp gmail";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(null, noHost));
        var noFrom = Gmail("a@gmail.com");
        noFrom.FromAddress = "nobody";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(null, noFrom));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SaveAsync(Guid.NewGuid(), Gmail("a@gmail.com")));
        Assert.Empty(await context.EmailAccounts.ToListAsync());
    }

    [Fact]
    public async Task A_test_email_goes_through_the_account_or_says_why_not()
    {
        using var context = Context();
        var service = Accounts(context);
        Assert.Equal(EmailQueueProcessor.NoAccount, await service.SendTestAsync(_college, "me@gmail.com"));

        await service.SaveAsync(null, Gmail("platform@gmail.com"));
        Assert.Null(await service.SendTestAsync(_college, "Me@Gmail.com"));
        var (account, message) = Assert.Single(_sender.Sent);
        Assert.Equal(("platform@gmail.com", "me@gmail.com"), (account.Username, message.To));
        Assert.Contains("platform default", message.Text);

        _sender.FailWith = new InvalidOperationException("535 Username and Password not accepted");
        Assert.Contains("535", await service.SendTestAsync(_college, "me@gmail.com"));
    }

    // ------------------------------------------------------------------ outbox and background sender

    private async Task WithAccount(int dailyLimit = 2000, string? examOffice = "exams@viva.in")
    {
        using var context = Context();
        await Accounts(context).SaveAsync(null, Gmail("platform@gmail.com"));
        context.CollegeSettings.Add(new CollegeSettings { Id = Guid.NewGuid(), CollegeId = _college, DailyEmailLimit = dailyLimit, ExamOfficeEmail = examOffice });
        await context.SaveChangesAsync();
    }

    private void Queue(ApplicationDbContext context, string to, int priority, DateTime? created = null, string kind = EmailKinds.Plain)
    {
        var item = new EmailOutbox(context, new EmailAccountResolver(context, _protector), _sender, NullLogger<EmailOutbox>.Instance)
            .Enqueue(_college, kind, null, to, priority, "Subject", "<p>Hi</p>", "Hi");
        if (created != null) item.CreatedAt = created.Value;
    }

    [Fact]
    public async Task OTPs_go_first_then_bulk_mail_oldest_first()
    {
        await WithAccount();
        using (var college = Context(_college))
        {
            Queue(college, "bulk1@mail.com", EmailPriorities.Low, DateTime.UtcNow.AddMinutes(-10));
            Queue(college, "otp@mail.com", EmailPriorities.High, DateTime.UtcNow);
            Queue(college, "bulk2@mail.com", EmailPriorities.Low, DateTime.UtcNow.AddMinutes(-5));
            await college.SaveChangesAsync();
        }

        using var background = Context(); // no signed-in user
        Assert.Equal(3, await Processor(background).ProcessAsync(10));

        Assert.Equal(new[] { "otp@mail.com", "bulk1@mail.com", "bulk2@mail.com" }, _sender.Sent.Select(s => s.Message.To));
        Assert.All(await background.EmailQueue.IgnoreQueryFilters().ToListAsync(), m =>
        {
            Assert.Equal(EmailStatuses.Sent, m.Status);
            Assert.NotNull(m.SentAt);
        });
    }

    [Fact]
    public async Task Over_the_daily_limit_bulk_mail_waits_for_tomorrow_but_an_OTP_still_goes()
    {
        await WithAccount(dailyLimit: 1);
        using (var college = Context(_college))
        {
            Queue(college, "bulk1@mail.com", EmailPriorities.Low, DateTime.UtcNow.AddMinutes(-3));
            Queue(college, "bulk2@mail.com", EmailPriorities.Low, DateTime.UtcNow.AddMinutes(-2));
            Queue(college, "otp@mail.com", EmailPriorities.High, DateTime.UtcNow.AddMinutes(-1));
            await college.SaveChangesAsync();
        }

        using var background = Context();
        await Processor(background).ProcessAsync(10);

        Assert.Equal(new[] { "otp@mail.com" }, _sender.Sent.Select(s => s.Message.To)); // the OTP made it 1 of 1 today
        var waiting = await background.EmailQueue.IgnoreQueryFilters().Where(m => m.Status == EmailStatuses.Pending).ToListAsync();
        Assert.Equal(2, waiting.Count);
        Assert.All(waiting, m =>
        {
            Assert.True(m.NextAttemptAt > DateTime.UtcNow.AddHours(-1) && m.NextAttemptAt >= DateTime.Today.AddDays(1).ToUniversalTime());
            Assert.Contains("limit", m.LastError);
        });
        var status = await new EmailOutbox(background, new EmailAccountResolver(background, _protector), _sender, NullLogger<EmailOutbox>.Instance).DailyStatusAsync(_college);
        Assert.Equal((1, 1, 2, 0), (status.Limit, status.SentToday, status.Queued, status.Remaining));
    }

    [Fact]
    public async Task Without_any_account_mail_waits_and_says_why()
    {
        using (var college = Context(_college))
        {
            Queue(college, "bulk@mail.com", EmailPriorities.Low);
            await college.SaveChangesAsync();
        }

        using var background = Context();
        await Processor(background).ProcessAsync(10);

        var item = await background.EmailQueue.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((EmailStatuses.Pending, 0, EmailQueueProcessor.NoAccount), (item.Status, item.Attempts, item.LastError));
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task A_failed_send_is_retried_later_and_marked_failed_after_five_attempts()
    {
        await WithAccount();
        using (var college = Context(_college))
        {
            Queue(college, "bulk@mail.com", EmailPriorities.Low);
            await college.SaveChangesAsync();
        }
        _sender.FailWith = new InvalidOperationException("Mailbox unavailable");

        using var background = Context();
        await Processor(background).ProcessAsync(10);
        var item = await background.EmailQueue.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((EmailStatuses.Pending, 1, "Mailbox unavailable"), (item.Status, item.Attempts, item.LastError));
        Assert.True(item.NextAttemptAt > DateTime.UtcNow.AddSeconds(30));

        for (int i = 0; i < 6; i++)
        {
            item.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await background.SaveChangesAsync();
            await Processor(background).ProcessAsync(10);
        }
        Assert.Equal((EmailStatuses.Failed, EmailQueueProcessor.MaxAttempts), (item.Status, item.Attempts));
    }

    [Fact]
    public async Task Link_emails_are_written_at_send_time_with_the_help_footer_and_dropped_when_no_longer_wanted()
    {
        await WithAccount();
        using (var college = Context(_college))
        {
            Queue(college, "ravi@mail.com", EmailPriorities.Low, kind: EmailKinds.TaskLink);
            await college.SaveChangesAsync();
        }
        using var background = Context();
        var renderer = new FakeRenderer();

        await Processor(background, renderer).ProcessAsync(10);

        var (_, message) = Assert.Single(_sender.Sent);
        Assert.Contains("For help, contact exams@viva.in.", message.Text);
        Assert.Contains("mailto:exams@viva.in", message.Html);
        Assert.DoesNotContain("<img", message.Html);

        using (var college = Context(_college))
        {
            Queue(college, "mina@mail.com", EmailPriorities.Low, kind: EmailKinds.TaskLink);
            await college.SaveChangesAsync();
        }
        renderer.Revoked = true;
        await Processor(background, renderer).ProcessAsync(10);
        Assert.Equal(EmailStatuses.Cancelled, (await background.EmailQueue.IgnoreQueryFilters().SingleAsync(m => m.ToAddress == "mina@mail.com")).Status);
    }

    [Fact]
    public async Task An_OTP_is_sent_at_once_and_stored_without_its_body_or_queued_first_in_line_when_sending_fails()
    {
        await WithAccount();
        using var college = Context(_college);
        var outbox = new EmailOutbox(college, new EmailAccountResolver(college, _protector), _sender, NullLogger<EmailOutbox>.Instance);
        var otp = EmailLayout.Build("ravi@mail.com", "Your code", "<p>Code 123456</p>", "Code 123456", null);

        Assert.True(await outbox.SendNowAsync(_college, EmailKinds.Otp, Guid.NewGuid(), otp));
        var sent = await college.EmailQueue.SingleAsync();
        Assert.Equal((EmailStatuses.Sent, EmailPriorities.High), (sent.Status, sent.Priority));
        Assert.Null(sent.HtmlBody);
        Assert.Null(sent.TextBody);

        _sender.FailWith = new TimeoutException("SMTP timed out");
        Assert.False(await outbox.SendNowAsync(_college, EmailKinds.Otp, Guid.NewGuid(), otp));
        var queued = await college.EmailQueue.SingleAsync(m => m.Status == EmailStatuses.Pending);
        Assert.Equal((EmailPriorities.High, EmailKinds.Otp, "SMTP timed out"), (queued.Priority, queued.Kind, queued.LastError));
        Assert.Null(queued.TextBody); // the code is written again at send time, never stored
    }
}
