using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using ExamAPI.Services.ExamApplications;
using ExamAPI.Services.Payments;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace ExamAPI.Tests
{
    /// <summary>
    /// Exam applications, steps 1–3 (docs/STUDENT_EXAM_APPLICATIONS.md): fees worked out only by FeeCalculator, staff fee
    /// setup rules, and the payment rules — the server sets the amount, only the gateway's own answer counts, confirming
    /// twice changes nothing, a wrong amount never counts.
    /// </summary>
    public class ExamPaymentTests
    {
        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 12, 1, 6, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }

        /// <summary>A gateway with keys, to prove the platform page never returns them.</summary>
        private sealed class KeyedGateway : IPaymentGateway
        {
            public string Provider => "KEYED";
            public string DisplayName => "Keyed";
            public bool NeedsMerchantId => true;
            public IReadOnlyList<GatewaySecretField> SecretFields => new[] { new GatewaySecretField("apiKey", "API key") };
            public bool AllowedInProduction => true;
            public Task<PaymentRedirect> StartAsync(GatewayAccount a, PaymentStartRequest r, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<GatewayStatus> GetStatusAsync(GatewayAccount a, string orderNo, CancellationToken ct = default) => throw new NotSupportedException();
        }

        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly Clock _clock = new();
        private readonly TestPaymentGateway _testGateway = new();
        private readonly SecretProtector _protector = new("unit-test-server-key-0123456789-abcdefghij");

        private readonly Guid _college = Guid.NewGuid();
        private readonly Guid _otherCollege = Guid.NewGuid();
        private readonly Guid _ayid = Guid.NewGuid();
        private readonly Guid _course = Guid.NewGuid();
        private readonly Guid _atktExam = Guid.NewGuid();
        private readonly Guid _regularExam = Guid.NewGuid();
        private readonly Guid _revalExam = Guid.NewGuid();
        private readonly Guid _student = Guid.NewGuid();
        private readonly Guid _scStudent = Guid.NewGuid();

        public ExamPaymentTests()
        {
            using var db = Context(null);
            db.Colleges.Add(new College { CollegeId = _college, Name = "VIVA", CollegeCode = "V", CollegeCenter = "Virar", ContactEmail = "a@a.in", ContactPhone = "1" });
            db.Colleges.Add(new College { CollegeId = _otherCollege, Name = "Other", CollegeCode = "O", CollegeCenter = "Vasai", ContactEmail = "b@b.in", ContactPhone = "2" });
            db.AcademicYears.Add(new AcademicYear { AYID = _ayid, FullDuration = "2026-2027", ShortDuration = "2026-27", CollegeId = _college });
            db.Exams.Add(new ExamMaster { ExamId = _regularExam, Name = "May 2026", ExamType = "Regular", CourseId = _course, AcademicYearAYID = _ayid, IsActive = true, CollegeId = _college });
            db.Exams.Add(new ExamMaster { ExamId = _atktExam, Name = "Dec 2026", ExamType = "A.T.K.T", CourseId = _course, AcademicYearAYID = _ayid, IsActive = true, CollegeId = _college });
            db.Exams.Add(new ExamMaster { ExamId = _revalExam, Name = "May 2026", ExamType = "Regular", RevaluationForExamId = _regularExam, CourseId = _course, AcademicYearAYID = _ayid, IsActive = true, CollegeId = _college });
            db.StudentMasters.Add(new StudentMaster { StdMstId = _student, StudentId = "2400001", FirstName = "Asha", LastName = "Patil", Email = "asha@demo.invalid", Category = "OPEN", CollegeId = _college });
            db.StudentMasters.Add(new StudentMaster { StdMstId = _scStudent, StudentId = "2400002", FirstName = "Ravi", LastName = "More", Email = "ravi@demo.invalid", Category = "SC", CollegeId = _college });
            db.SaveChanges();
        }

        private ApplicationDbContext Context(Guid? college)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(_dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var user = new Mock<ICurrentUser>();
            var captured = college;
            user.SetupGet(u => u.CollegeId).Returns(captured);
            return new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, user.Object);
        }

        private static IHostEnvironment Env(string name)
        {
            var env = new Mock<IHostEnvironment>();
            env.SetupGet(e => e.EnvironmentName).Returns(name);
            return env.Object;
        }

        private PaymentAccountResolver Resolver(ApplicationDbContext db, string env = "Development") =>
            new(db, _protector, new IPaymentGateway[] { _testGateway, new KeyedGateway() }, Env(env));

        private PaymentService Payments(ApplicationDbContext db, string env = "Development") => new(db, Resolver(db, env), _clock);

        private static ExamFeeInput Fees(decimal perSubject = 300, decimal fixedFee = 60) => new()
        {
            ClosesAt = new DateTimeOffset(2026, 12, 15, 23, 59, 0, TimeSpan.FromHours(5.5)),
            PerSubjectFee = perSubject,
            FixedFee = fixedFee,
            FixedFeeLabel = fixedFee > 0 ? "Statement of marks" : null,
        };

        private async Task SetFeesAsync(ExamFeeInput input, Guid? exam = null)
        {
            using var db = Context(_college);
            await new ExamFeeService(db).SaveAsync(exam ?? _atktExam, input);
        }

        private async Task AddTestAccountAsync(bool platformDefault = false, bool enabled = true)
        {
            using var db = Context(null);
            db.PaymentAccounts.Add(new PaymentAccount { Id = Guid.NewGuid(), CollegeId = platformDefault ? null : _college, Provider = PaymentProviders.Test, IsTestMode = true, IsEnabled = enabled, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        /// <summary>A draft application with N subjects; AmountDue is deliberately wrong (as if a browser had sent it).</summary>
        private async Task<Guid> ApplyAsync(Guid? student = null, int subjects = 2, Guid? exam = null)
        {
            using var db = Context(_college);
            var app = new ExamApplication
            {
                Id = Guid.NewGuid(), CollegeId = _college, StdMstId = student ?? _student, TargetExamId = exam ?? _atktExam,
                Kind = exam == _revalExam ? ExamApplicationKinds.Revaluation : ExamApplicationKinds.Atkt, AmountDue = 1,
            };
            for (var i = 0; i < subjects; i++)
                app.Subjects.Add(new ExamApplicationSubject { Id = Guid.NewGuid(), CollegeId = _college, SubjectId = Guid.NewGuid() });
            db.ExamApplications.Add(app);
            await db.SaveChangesAsync();
            return app.Id;
        }

        private async Task<StartPaymentResult> StartAsync(Guid appId)
        {
            using var db = Context(_college);
            return await Payments(db).StartAsync(appId, "https://api.test/", "https://site.test/app");
        }

        private async Task<PaymentOrder> ConfirmAsync(string orderNo)
        {
            using var db = Context(null); // the gateway's return carries no sign-in
            return await Payments(db).ConfirmAsync(orderNo);
        }

        private ExamApplication Application(Guid id)
        {
            using var db = Context(null);
            return db.ExamApplications.IgnoreQueryFilters().AsNoTracking().Single(a => a.Id == id);
        }

        // =================================================================
        // Fee rules
        // =================================================================

        [Fact]
        public void Fee_is_per_subject_plus_fixed_with_cap_late_fee_and_category_rates()
        {
            var setup = new ExamFeeSetup
            {
                Kind = ExamApplicationKinds.Atkt, ClosesAt = DateTime.UtcNow.AddDays(10), PerSubjectFee = 300, MaxSubjectFee = 1000,
                FixedFee = 60, FixedFeeLabel = "Statement of marks", LateFeeFrom = new DateTime(2026, 12, 10), LateFee = 100,
                CategoryRates =
                {
                    new ExamFeeCategoryRate { Category = "OBC", PerSubjectFee = 150 },
                    new ExamFeeCategoryRate { Category = "SC", IsExempt = true },
                },
            };
            var onTime = new DateTime(2026, 12, 1);
            var late = new DateTime(2026, 12, 11);

            Assert.Equal(2 * 300 + 60, FeeCalculator.Compute(setup, "OPEN", 2, onTime).Total);
            Assert.Equal(1000 + 60, FeeCalculator.Compute(setup, "OPEN", 5, onTime).Total);          // capped
            Assert.Equal(2 * 300 + 60 + 100, FeeCalculator.Compute(setup, "OPEN", 2, late).Total);   // late fee
            Assert.Equal(2 * 150 + 60, FeeCalculator.Compute(setup, " obc ", 2, onTime).Total);      // category rate, any case
            var sc = FeeCalculator.Compute(setup, "SC", 3, late);
            Assert.True(sc.IsExempt);
            Assert.Equal(0, sc.Total);
        }

        // =================================================================
        // Fee setup (staff)
        // =================================================================

        [Fact]
        public async Task Fees_can_be_set_only_for_atkt_reexam_or_revaluation_exams_with_a_closing_date()
        {
            using var db = Context(_college);
            var service = new ExamFeeService(db);

            var noClose = Fees();
            noClose.ClosesAt = null;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(_atktExam, noClose));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(_regularExam, Fees()));

            var lateAfterClose = Fees();
            lateAfterClose.LateFee = 100;
            lateAfterClose.LateFeeFrom = lateAfterClose.ClosesAt!.Value.AddDays(1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(_atktExam, lateAfterClose));

            var atkt = await service.SaveAsync(_atktExam, Fees());
            Assert.Equal(ExamApplicationKinds.Atkt, atkt.Kind);
            Assert.Equal(new DateTime(2026, 12, 15, 18, 29, 0), atkt.ClosesAt); // 23:59 IST stored as UTC
            var reval = await service.SaveAsync(_revalExam, Fees(perSubject: 500, fixedFee: 0));
            Assert.Equal(ExamApplicationKinds.Revaluation, reval.Kind);

            var exams = await service.GetExamsAsync(_course, _ayid);
            Assert.Equal(new[] { _atktExam, _revalExam }.OrderBy(x => x), exams.Select(e => e.ExamId).OrderBy(x => x));
        }

        [Fact]
        public async Task Another_college_cannot_see_or_change_the_fees()
        {
            await SetFeesAsync(Fees());
            using var db = Context(_otherCollege);
            var service = new ExamFeeService(db);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(_atktExam));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SaveAsync(_atktExam, Fees(perSubject: 1)));
        }

        [Fact]
        public async Task Once_a_student_has_paid_the_amounts_are_locked_but_the_dates_can_move()
        {
            await SetFeesAsync(Fees());
            await AddTestAccountAsync();
            var app = await ApplyAsync();
            var start = await StartAsync(app);
            _testGateway.Decide(start.OrderNo!, TestPaymentGateway.Outcome.Success);
            await ConfirmAsync(start.OrderNo!);

            using var db = Context(_college);
            var service = new ExamFeeService(db);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(_atktExam, Fees(perSubject: 400)));
            var later = Fees();
            later.ClosesAt = later.ClosesAt!.Value.AddDays(5);
            var saved = await service.SaveAsync(_atktExam, later);
            Assert.True(saved.AmountsLocked);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(_atktExam));
        }

        // =================================================================
        // Payments
        // =================================================================

        [Fact]
        public async Task The_server_sets_the_amount_and_only_the_gateways_answer_makes_it_paid()
        {
            await SetFeesAsync(Fees());
            await AddTestAccountAsync();
            var app = await ApplyAsync(subjects: 2);

            var start = await StartAsync(app);
            Assert.False(start.Paid);
            Assert.Equal(2 * 300 + 60, start.Amount); // not the 1 on the application
            Assert.StartsWith("https://api.test/api/Payments/test-gateway/GS", start.Redirect!.Url);

            // The browser comes back before the gateway decided: not paid, re-checked later.
            var pending = await ConfirmAsync(start.OrderNo!);
            Assert.Equal(PaymentOrderStatuses.Pending, pending.Status);
            Assert.Equal(ExamApplicationStatuses.Draft, Application(app).Status);

            _testGateway.Decide(start.OrderNo!, TestPaymentGateway.Outcome.Success);
            var paid = await ConfirmAsync(start.OrderNo!);
            Assert.Equal(PaymentOrderStatuses.Success, paid.Status);
            var a = Application(app);
            Assert.Equal(ExamApplicationStatuses.Paid, a.Status);
            Assert.Equal(PaidVia.Online, a.PaidVia);
            Assert.Equal("AT/2026-27/00001", a.ReceiptNo);
            Assert.Equal(660, a.AmountDue);

            // Confirming again (browser refresh, background check) changes nothing.
            await ConfirmAsync(start.OrderNo!);
            Assert.Equal("AT/2026-27/00001", Application(app).ReceiptNo);
            Assert.False(Application(app).PaidTwice);

            using var db = Context(_college);
            Assert.Equal("https://site.test/app/student?payment=" + start.OrderNo, Payments(db).ResultUrl(paid));
        }

        [Fact]
        public async Task A_success_with_the_wrong_amount_never_counts()
        {
            await SetFeesAsync(Fees());
            await AddTestAccountAsync();
            var app = await ApplyAsync();
            var start = await StartAsync(app);
            _testGateway.Decide(start.OrderNo!, TestPaymentGateway.Outcome.WrongAmount);

            var order = await ConfirmAsync(start.OrderNo!);
            Assert.Equal(PaymentOrderStatuses.Mismatch, order.Status);
            Assert.Equal(ExamApplicationStatuses.Draft, Application(app).Status);
            Assert.Null(Application(app).ReceiptNo);
        }

        [Fact]
        public async Task A_failed_payment_can_be_tried_again_and_a_second_success_is_flagged_not_counted()
        {
            await SetFeesAsync(Fees());
            await AddTestAccountAsync();
            var app = await ApplyAsync();

            var first = await StartAsync(app);
            _testGateway.Decide(first.OrderNo!, TestPaymentGateway.Outcome.Failed);
            Assert.Equal(PaymentOrderStatuses.Failed, (await ConfirmAsync(first.OrderNo!)).Status);

            var second = await StartAsync(app);
            Assert.NotEqual(first.OrderNo, second.OrderNo);
            var third = await StartAsync(app); // two tabs: two open orders, both paid
            _testGateway.Decide(second.OrderNo!, TestPaymentGateway.Outcome.Success);
            _testGateway.Decide(third.OrderNo!, TestPaymentGateway.Outcome.Success);
            await ConfirmAsync(second.OrderNo!);
            await ConfirmAsync(third.OrderNo!);

            var a = Application(app);
            Assert.Equal(ExamApplicationStatuses.Paid, a.Status);
            Assert.Equal("AT/2026-27/00001", a.ReceiptNo);
            Assert.True(a.PaidTwice);

            // Once paid, "Pay" asks nothing more of the gateway.
            var again = await StartAsync(app);
            Assert.True(again.Paid);
            Assert.Null(again.Redirect);
        }

        [Fact]
        public async Task An_exempt_category_is_accepted_without_a_payment()
        {
            var input = Fees();
            input.CategoryRates.Add(new ExamFeeCategoryRateDto { Category = "SC", IsExempt = true });
            await SetFeesAsync(input);
            var app = await ApplyAsync(student: _scStudent); // no gateway account needed

            var start = await StartAsync(app);
            Assert.True(start.Paid);
            var a = Application(app);
            Assert.Equal(PaidVia.Waived, a.PaidVia);
            Assert.Equal(0, a.AmountDue);
            Assert.NotNull(a.ReceiptNo);
        }

        [Fact]
        public async Task Payment_refused_outside_the_window_or_without_a_gateway_account()
        {
            await SetFeesAsync(Fees());
            var app = await ApplyAsync();
            var none = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(app));
            Assert.Contains("isn't set up", none.Message);

            await AddTestAccountAsync();
            _clock.Now = new DateTimeOffset(2026, 12, 16, 0, 0, 0, TimeSpan.Zero);
            var closed = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(app));
            Assert.Contains("closed", closed.Message);
        }

        [Fact]
        public async Task A_college_uses_its_own_account_else_the_default_and_the_test_gateway_never_in_production()
        {
            await AddTestAccountAsync(platformDefault: true);
            using (var db = Context(null))
            {
                var (acc, isDefault) = await Resolver(db).ForCollegeAsync(_college);
                Assert.True(isDefault);
                Assert.Null(acc!.CollegeId);
            }

            await AddTestAccountAsync(enabled: false); // the college's own account, switched off
            using (var db = Context(null))
                Assert.True((await Resolver(db).ForCollegeAsync(_college)).IsDefault);

            using (var db = Context(null))
            {
                db.PaymentAccounts.Single(a => a.CollegeId == _college).IsEnabled = true;
                await db.SaveChangesAsync();
                var (acc, isDefault) = await Resolver(db).ForCollegeAsync(_college);
                Assert.False(isDefault);
                Assert.Equal(_college, acc!.CollegeId);

                Assert.NotNull(Resolver(db).Gateway(PaymentProviders.Test));
                Assert.Null(Resolver(db, "Production").Gateway(PaymentProviders.Test));
            }
        }

        [Fact]
        public async Task Open_orders_are_rechecked_in_the_background_and_expire_after_48_hours()
        {
            await SetFeesAsync(Fees());
            await AddTestAccountAsync();
            var app = await ApplyAsync();
            var start = await StartAsync(app);
            _testGateway.Decide(start.OrderNo!, TestPaymentGateway.Outcome.Pending);

            _clock.Now = _clock.Now.AddMinutes(11);
            using (var db = Context(null))
                Assert.Equal(1, await Payments(db).RecheckDueAsync(20));
            using (var db = Context(null))
                Assert.Equal(PaymentOrderStatuses.Pending, db.PaymentOrders.IgnoreQueryFilters().Single().Status);

            _clock.Now = _clock.Now.AddHours(49);
            using (var db = Context(null))
                await Payments(db).RecheckDueAsync(20);
            using (var db = Context(null))
                Assert.Equal(PaymentOrderStatuses.Expired, db.PaymentOrders.IgnoreQueryFilters().Single().Status);
            Assert.Equal(ExamApplicationStatuses.Draft, Application(app).Status);
        }

        [Fact]
        public async Task Gateway_keys_are_stored_encrypted_and_never_returned()
        {
            using var db = Context(null);
            var resolver = Resolver(db);
            var service = new PaymentAccountService(db, _protector, resolver);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(_college, new PaymentAccountInput { Provider = "KEYED", MerchantId = "M1" }));
            var saved = await service.SaveAsync(_college, new PaymentAccountInput
            {
                Provider = "KEYED", MerchantId = "M1", IsTestMode = false,
                Secrets = new Dictionary<string, string?> { ["apiKey"] = "s3cret-value" },
            });
            Assert.Equal(new[] { "apiKey" }, saved.SavedSecrets);
            Assert.DoesNotContain("s3cret-value", System.Text.Json.JsonSerializer.Serialize(saved));
            var stored = db.PaymentAccounts.Single(a => a.CollegeId == _college);
            Assert.DoesNotContain("s3cret-value", stored.SecretsProtected);

            // Blank keeps the saved key.
            await service.SaveAsync(_college, new PaymentAccountInput { Provider = "KEYED", MerchantId = "M2", Secrets = new() { ["apiKey"] = "" } });
            Assert.Equal("s3cret-value", resolver.Open(db.PaymentAccounts.Single(a => a.CollegeId == _college)).Secrets["apiKey"]);

            // The test gateway can't be saved on the live server.
            var live = new PaymentAccountService(db, _protector, Resolver(db, "Production"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => live.SaveAsync(null, new PaymentAccountInput { Provider = PaymentProviders.Test }));
        }
    }
}
