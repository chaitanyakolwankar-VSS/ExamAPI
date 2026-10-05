using System.Reflection;
using System.Security.Claims;
using ExamAPI.Controllers;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Auth;
using ExamAPI.Services.Files;
using ExamAPI.Services.Platform;
using ExamAPI.Services.Tenancy;
using ExamAPI.Services.UsersMaster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// T-08: the platform page's API. ProvisionCollegeService (C# port of ProvisionPharmacyCollege.sql)
/// builds a usable college, clones the grade scale and ordinance rule sets from a template college
/// with ids remapped, and is idempotent on the college code. The platform admin has no CollegeId,
/// so the test context is the platform view: tenant rows are only reachable through
/// IgnoreQueryFilters(), exactly as in production.
/// </summary>
public sealed class PlatformProvisionTests
{
    private sealed class Rig
    {
        public required ApplicationDbContext Context { get; init; }
        public required DbContextOptions<ApplicationDbContext> Options { get; init; }
        public required ProvisionCollegeService Provision { get; init; }
        public required PlatformCollegeService Colleges { get; init; }
        public required Mock<IFileStorage> Storage { get; init; }
        public required Guid TemplateId { get; init; }
        public required Guid TemplateGradeId { get; init; }
        public required Guid TemplateRuleSetId { get; init; }

        public ApplicationDbContext ScopedTo(Guid college)
        {
            var cu = new Mock<ICurrentUser>();
            cu.SetupGet(u => u.CollegeId).Returns(college);
            return new ApplicationDbContext(Options, new Mock<IHttpContextAccessor>().Object, cu.Object);
        }
    }

    private static Rig NewRig()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.IsPlatformAdmin).Returns(true); // no CollegeId: the platform view
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ctx = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);

        // ---- template college with a grade scale and two rule sets (one empty leftover) ----
        var tpl = new College { CollegeId = Guid.NewGuid(), Name = "Template", CollegeCode = "TPL", CollegeCenter = "T", ContactEmail = "t@t.edu", ContactPhone = "1" };
        ctx.Colleges.Add(tpl);

        var pattern = new PatternMaster { PatternId = Guid.NewGuid(), PatternName = "NEP", CollegeId = tpl.CollegeId };
        var grade = new GradeMaster { GradeMasterId = Guid.NewGuid(), Name = "10 point", Description = "UoM", CollegeId = tpl.CollegeId };
        ctx.PatternMasters.Add(pattern);
        ctx.GradeMasters.Add(grade);
        ctx.GradeThresholds.AddRange(
            new GradeThreshold { ThresholdId = Guid.NewGuid(), Grade = "O", GradePoint = 10, MinPercentage = 80, MaxPercentage = 100, PerformanceRemark = "Outstanding", GradeMasterId = grade.GradeMasterId },
            new GradeThreshold { ThresholdId = Guid.NewGuid(), Grade = "A", GradePoint = 9, MinPercentage = 70, MaxPercentage = 79.99m, PerformanceRemark = "Excellent", GradeMasterId = grade.GradeMasterId },
            new GradeThreshold { ThresholdId = Guid.NewGuid(), Grade = "F", GradePoint = 0, MinPercentage = 0, MaxPercentage = 39.99m, PerformanceRemark = "Fail", GradeMasterId = grade.GradeMasterId, IsDeleted = true });

        var ruleSet = new RuleSet { RuleSetId = Guid.NewGuid(), Name = "Ordinances", ExamType = "Regular", IsActive = true, PatternId = pattern.PatternId, GradeMasterId = grade.GradeMasterId, CollegeId = tpl.CollegeId };
        var emptySet = new RuleSet { RuleSetId = Guid.NewGuid(), Name = "Leftover", IsActive = true, PatternId = pattern.PatternId, CollegeId = tpl.CollegeId };
        ctx.RuleSets.AddRange(ruleSet, emptySet);
        for (var i = 1; i <= 2; i++)
        {
            var rule = new Rule { RuleId = Guid.NewGuid(), Name = $"O.{i}", Priority = i, IsEnabled = true, StopOnSuccess = i == 1, OrdinanceSymbol = $"#{i}", RuleSetId = ruleSet.RuleSetId };
            ctx.Rules.Add(rule);
            ctx.RuleConditions.Add(new RuleCondition { ConditionId = Guid.NewGuid(), FactName = "TotalMarks", Operator = "<", Value = "40", RuleId = rule.RuleId });
            ctx.RuleActions.Add(new RuleAction { ActionId = Guid.NewGuid(), ActionType = "AddGrace", CalculationMode = "Fixed", Param1Type = "Value", Param1Value = 5.5m, MaxLimit = 7.75m, MaxTargetCount = 2, Target = "Subject", Expression = "x+1", RuleId = rule.RuleId });
        }
        ctx.SaveChanges();

        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.SaveAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((byte[] _, string folder, string name, CancellationToken _) => $"{folder}/{name}");

        var users = new UserMasterService(ctx);
        return new Rig
        {
            Context = ctx,
            Options = options,
            Storage = storage,
            Provision = new ProvisionCollegeService(ctx, users, storage.Object),
            Colleges = new PlatformCollegeService(ctx, users),
            TemplateId = tpl.CollegeId,
            TemplateGradeId = grade.GradeMasterId,
            TemplateRuleSetId = ruleSet.RuleSetId,
        };
    }

    private static ProvisionCollegeRequest Request(Rig rig, Action<ProvisionCollegeRequest>? tweak = null)
    {
        var r = new ProvisionCollegeRequest
        {
            Name = "Test College",
            CollegeCode = "TST001",
            CollegeCenter = "Mumbai",
            Address = "1 Main Road",
            ContactEmail = "contact@test.edu",
            ContactPhone = "9000000000",
            AcademicYear = new AcademicYearInput { FullDuration = "2024-2025", ShortDuration = "24-25", IsCurrent = true },
            Branches = new() { new BranchInput { Name = "Test Branch", Code = "TB" } },
            Patterns = new() { "NEP" },
            TemplateCollegeId = rig.TemplateId,
            Admins = new() { Admin("one") },
        };
        tweak?.Invoke(r);
        return r;
    }

    private static AdminInput Admin(string name) => new()
    {
        Username = name, Email = $"{name}@test.edu", FirstName = name, LastName = "Admin", Password = "Password@123",
    };

    private static IFormFile Png(string name, int bytes = 16, string type = "image/png")
    {
        var stream = new MemoryStream(new byte[bytes]);
        return new FormFile(stream, 0, bytes, name, $"{name}.png") { Headers = new HeaderDictionary(), ContentType = type };
    }

    private static async Task<int> Count(ApplicationDbContext c, Guid college) =>
        await c.AcademicYears.IgnoreQueryFilters().CountAsync(x => x.CollegeId == college)
        + await c.CourseMasters.IgnoreQueryFilters().CountAsync(x => x.CollegeId == college)
        + await c.PatternMasters.IgnoreQueryFilters().CountAsync(x => x.CollegeId == college)
        + await c.GradeMasters.IgnoreQueryFilters().CountAsync(x => x.CollegeId == college)
        + await c.RuleSets.IgnoreQueryFilters().CountAsync(x => x.CollegeId == college)
        + await c.RoleMasters.IgnoreQueryFilters().CountAsync(x => x.CollegeId == college)
        + await c.UserMasters.IgnoreQueryFilters().CountAsync(x => x.CollegeId == college);

    private static int Created(ProvisionSummary s, string item) => s.Items.Single(i => i.Item == item).Created;
    private static int Present(ProvisionSummary s, string item) => s.Items.Single(i => i.Item == item).AlreadyPresent;

    // ---------------------------------------------------------------- provisioning

    [Fact]
    public async Task Provision_creates_every_piece_of_a_usable_college()
    {
        var rig = NewRig();
        var summary = await rig.Provision.ProvisionAsync(Request(rig, r => { r.Logo = Png("logo"); r.Banner = Png("banner"); }));
        var id = summary.CollegeId;
        var db = rig.Context;

        Assert.False(summary.AlreadyExisted);
        var college = await db.Colleges.IgnoreQueryFilters().SingleAsync(c => c.CollegeId == id);
        Assert.Equal("Test College", college.Name);
        Assert.Equal("TST001", college.CollegeCode);
        Assert.StartsWith("college/logos/", college.LogoUrl);
        Assert.StartsWith("college/banners/", college.LogoBannerUrl);

        var ay = await db.AcademicYears.IgnoreQueryFilters().SingleAsync(a => a.CollegeId == id);
        Assert.True(ay.IsCurrent);
        Assert.Equal("2024-2025", ay.FullDuration);
        Assert.Equal("24-25", ay.ShortDuration);

        var course = await db.CourseMasters.IgnoreQueryFilters().SingleAsync(c => c.CollegeId == id);
        Assert.Equal(("Test Branch", "TB"), (course.Name, course.CourseCode));
        Assert.Equal("NEP", (await db.PatternMasters.IgnoreQueryFilters().SingleAsync(p => p.CollegeId == id)).PatternName);

        Assert.Single(await db.GradeMasters.IgnoreQueryFilters().Where(g => g.CollegeId == id).ToListAsync());
        Assert.Single(await db.RuleSets.IgnoreQueryFilters().Where(r => r.CollegeId == id).ToListAsync());

        var role = await db.RoleMasters.IgnoreQueryFilters().SingleAsync(r => r.CollegeId == id);
        Assert.Equal("Admin", role.Name);

        var admin = await db.UserMasters.IgnoreQueryFilters().SingleAsync(u => u.CollegeId == id);
        Assert.Equal(role.RoleId, admin.RoleId);
        Assert.Equal("one@test.edu", admin.Email);
        Assert.False(admin.IsPlatformAdmin);
        Assert.True(BCrypt.Net.BCrypt.Verify("Password@123", admin.HashedPassword));   // hashed through UserMasterService

        Assert.Equal(1, Created(summary, "College"));
        Assert.Equal(1, Created(summary, "Academic year"));
        Assert.Equal(1, Created(summary, "Admins"));
        Assert.Single(summary.Admins);
        Assert.Equal("created", summary.Admins[0].Status);
    }

    [Fact]
    public async Task Provision_clones_grade_scale_and_rule_sets_with_remapped_ids_and_leaves_the_template_untouched()
    {
        var rig = NewRig();
        var db = rig.Context;
        var before = (
            Grades: await db.GradeMasters.IgnoreQueryFilters().Where(g => g.CollegeId == rig.TemplateId).Select(g => g.GradeMasterId).ToListAsync(),
            Thresholds: await db.GradeThresholds.IgnoreQueryFilters().CountAsync(),
            RuleSets: await db.RuleSets.IgnoreQueryFilters().Where(r => r.CollegeId == rig.TemplateId).Select(r => r.RuleSetId).ToListAsync(),
            Rules: await db.Rules.IgnoreQueryFilters().Select(r => r.RuleId).ToListAsync(),
            Conditions: await db.RuleConditions.IgnoreQueryFilters().CountAsync(),
            Actions: await db.RuleActions.IgnoreQueryFilters().CountAsync(),
            Patterns: await db.PatternMasters.IgnoreQueryFilters().Where(p => p.CollegeId == rig.TemplateId).Select(p => p.PatternId).ToListAsync());

        var summary = await rig.Provision.ProvisionAsync(Request(rig));
        var id = summary.CollegeId;

        var grade = await db.GradeMasters.IgnoreQueryFilters().SingleAsync(g => g.CollegeId == id);
        Assert.NotEqual(rig.TemplateGradeId, grade.GradeMasterId);
        Assert.Equal("10 point", grade.Name);
        var thresholds = await db.GradeThresholds.IgnoreQueryFilters().Where(t => t.GradeMasterId == grade.GradeMasterId).ToListAsync();
        Assert.Equal(new[] { "A", "O" }, thresholds.Select(t => t.Grade).OrderBy(x => x));   // the soft-deleted "F" is not cloned
        Assert.Equal(79.99m, thresholds.Single(t => t.Grade == "A").MaxPercentage);
        Assert.Equal("Outstanding", thresholds.Single(t => t.Grade == "O").PerformanceRemark);

        var pattern = await db.PatternMasters.IgnoreQueryFilters().SingleAsync(p => p.CollegeId == id);
        var set = await db.RuleSets.IgnoreQueryFilters().SingleAsync(r => r.CollegeId == id);   // the empty "Leftover" set is skipped
        Assert.NotEqual(rig.TemplateRuleSetId, set.RuleSetId);
        Assert.Equal("Ordinances", set.Name);
        Assert.Equal(pattern.PatternId, set.PatternId);              // remapped to the NEW college's pattern (by name)
        Assert.NotEqual(before.Patterns.Single(), set.PatternId);
        Assert.Equal(grade.GradeMasterId, set.GradeMasterId);        // remapped to the cloned grade scale
        Assert.True(set.IsActive);
        Assert.Contains(summary.Warnings, w => w.Contains("Leftover"));

        var rules = await db.Rules.IgnoreQueryFilters().Where(r => r.RuleSetId == set.RuleSetId).OrderBy(r => r.Priority).ToListAsync();
        Assert.Equal(new[] { "O.1", "O.2" }, rules.Select(r => r.Name));
        Assert.True(rules[0].StopOnSuccess);
        Assert.Equal("#2", rules[1].OrdinanceSymbol);
        foreach (var rule in rules)
        {
            var cond = await db.RuleConditions.IgnoreQueryFilters().SingleAsync(c => c.RuleId == rule.RuleId);
            Assert.Equal(("TotalMarks", "<", "40"), (cond.FactName, cond.Operator, cond.Value));
            var act = await db.RuleActions.IgnoreQueryFilters().SingleAsync(a => a.RuleId == rule.RuleId);
            Assert.Equal(("AddGrace", "Fixed", 5.5m, 7.75m, 2, "Subject", "x+1"),
                (act.ActionType, act.CalculationMode, act.Param1Value, act.MaxLimit, act.MaxTargetCount, act.Target, act.Expression));
        }
        Assert.DoesNotContain(rules.Select(r => r.RuleId), id => before.Rules.Contains(id));   // new rows, not shared ones

        Assert.Equal(1, Created(summary, "Grade scales"));
        Assert.Equal(2, Created(summary, "Grade thresholds"));
        Assert.Equal(1, Created(summary, "Rule sets"));
        Assert.Equal(2, Created(summary, "Rules"));
        Assert.Equal(2, Created(summary, "Rule conditions"));
        Assert.Equal(2, Created(summary, "Rule actions"));

        // ---- the template is exactly as it was ----
        Assert.Equal(before.Grades, await db.GradeMasters.IgnoreQueryFilters().Where(g => g.CollegeId == rig.TemplateId).Select(g => g.GradeMasterId).ToListAsync());
        Assert.Equal(before.RuleSets, await db.RuleSets.IgnoreQueryFilters().Where(r => r.CollegeId == rig.TemplateId).Select(r => r.RuleSetId).ToListAsync());
        Assert.Equal(before.Patterns, await db.PatternMasters.IgnoreQueryFilters().Where(p => p.CollegeId == rig.TemplateId).Select(p => p.PatternId).ToListAsync());
        Assert.Equal(before.Thresholds + 2, await db.GradeThresholds.IgnoreQueryFilters().CountAsync());
        Assert.Equal(before.Conditions + 2, await db.RuleConditions.IgnoreQueryFilters().CountAsync());
        Assert.Equal(before.Actions + 2, await db.RuleActions.IgnoreQueryFilters().CountAsync());
        var tplSet = await db.RuleSets.IgnoreQueryFilters().SingleAsync(r => r.RuleSetId == rig.TemplateRuleSetId);
        Assert.Equal(rig.TemplateGradeId, tplSet.GradeMasterId);
        Assert.Equal(before.Patterns.Single(), tplSet.PatternId);
        Assert.Equal(2, await db.Rules.IgnoreQueryFilters().CountAsync(r => r.RuleSetId == rig.TemplateRuleSetId));
    }

    [Fact]
    public async Task Rerunning_the_same_college_code_creates_nothing_new()
    {
        var rig = NewRig();
        var first = await rig.Provision.ProvisionAsync(Request(rig));
        var counts = await Count(rig.Context, first.CollegeId);
        var totals = (
            rig.Context.GradeThresholds.IgnoreQueryFilters().Count(),
            rig.Context.Rules.IgnoreQueryFilters().Count(),
            rig.Context.RuleConditions.IgnoreQueryFilters().Count(),
            rig.Context.RuleActions.IgnoreQueryFilters().Count(),
            rig.Context.Colleges.IgnoreQueryFilters().Count());

        // Same code (any casing), same payload.
        var second = await rig.Provision.ProvisionAsync(Request(rig, r => r.CollegeCode = "tst001"));

        Assert.Equal(first.CollegeId, second.CollegeId);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(counts, await Count(rig.Context, first.CollegeId));
        Assert.Equal(totals, (
            rig.Context.GradeThresholds.IgnoreQueryFilters().Count(),
            rig.Context.Rules.IgnoreQueryFilters().Count(),
            rig.Context.RuleConditions.IgnoreQueryFilters().Count(),
            rig.Context.RuleActions.IgnoreQueryFilters().Count(),
            rig.Context.Colleges.IgnoreQueryFilters().Count()));
        Assert.All(second.Items, i => Assert.Equal(0, i.Created));
        Assert.Equal(1, Present(second, "Rule sets"));
        Assert.Equal(1, Present(second, "Admins"));
        Assert.Equal("already present", second.Admins.Single().Status);
        Assert.Equal(first.Admins.Single().UserId, second.Admins.Single().UserId);
    }

    [Fact]
    public async Task Rerun_fills_only_the_missing_parts_and_never_overwrites()
    {
        var rig = NewRig();
        var first = await rig.Provision.ProvisionAsync(Request(rig, r => { r.Admins = new(); r.Logo = Png("logo"); }));
        Assert.Empty(await rig.Context.UserMasters.IgnoreQueryFilters().Where(u => u.CollegeId == first.CollegeId).ToListAsync());

        var second = await rig.Provision.ProvisionAsync(Request(rig, r =>
        {
            r.Name = "Renamed College";                        // existing college is not edited
            r.Branches = new() { new() { Name = "Test Branch", Code = "TB" }, new() { Name = "Second", Code = "SB" } };
            r.Patterns = new() { "NEP", "CBCS" };
            r.AcademicYear = new AcademicYearInput { FullDuration = "2025-2026", IsCurrent = true };
            r.Logo = Png("other");                              // already has a logo: kept
            r.Banner = Png("banner");                           // missing: filled
        }));

        var db = rig.Context;
        Assert.Equal("Test College", (await db.Colleges.IgnoreQueryFilters().SingleAsync(c => c.CollegeId == second.CollegeId)).Name);
        Assert.Equal(2, await db.CourseMasters.IgnoreQueryFilters().CountAsync(c => c.CollegeId == second.CollegeId));
        Assert.Equal(2, await db.PatternMasters.IgnoreQueryFilters().CountAsync(c => c.CollegeId == second.CollegeId));
        Assert.Equal(1, Created(second, "Branches"));
        Assert.Equal(1, Present(second, "Branches"));
        Assert.Equal(1, Present(second, "Logo"));
        Assert.Equal(1, Created(second, "Banner"));
        rig.Storage.Verify(s => s.SaveAsync(It.IsAny<byte[]>(), "college/logos", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // The 2024-25 year was already current, so the new year does not steal "current".
        var years = await db.AcademicYears.IgnoreQueryFilters().Where(a => a.CollegeId == second.CollegeId).ToListAsync();
        Assert.Equal(2, years.Count);
        Assert.Equal("2024-2025", years.Single(a => a.IsCurrent).FullDuration);
    }

    [Fact]
    public async Task A_college_user_sees_the_provisioned_data_and_another_college_sees_none_of_it()
    {
        var rig = NewRig();
        var summary = await rig.Provision.ProvisionAsync(Request(rig));
        var mine = rig.ScopedTo(summary.CollegeId);

        Assert.True(await mine.AcademicYears.AnyAsync(a => a.IsCurrent));
        Assert.Equal(1, await mine.CourseMasters.CountAsync());
        Assert.Equal(1, await mine.PatternMasters.CountAsync());
        Assert.Equal(1, await mine.GradeMasters.CountAsync());
        Assert.Equal(1, await mine.RuleSets.CountAsync());
        Assert.Equal(2, await mine.Rules.CountAsync(r => r.RuleSet!.CollegeId == summary.CollegeId));
        Assert.Equal(1, await mine.RoleMasters.CountAsync());
        Assert.Equal(1, await mine.UserMasters.CountAsync());

        var other = rig.ScopedTo(Guid.NewGuid());
        Assert.Equal(0, await other.AcademicYears.CountAsync());
        Assert.Equal(0, await other.CourseMasters.CountAsync());
        Assert.Equal(0, await other.RuleSets.CountAsync());
    }

    [Fact]
    public async Task Provision_rejects_bad_input_with_a_clear_message_and_writes_nothing()
    {
        var rig = NewRig();
        var colleges = await rig.Context.Colleges.IgnoreQueryFilters().CountAsync();

        async Task<string> Fail(Action<ProvisionCollegeRequest> tweak) =>
            (await Assert.ThrowsAnyAsync<Exception>(() => rig.Provision.ProvisionAsync(Request(rig, tweak)))).Message;

        Assert.Contains("branch", await Fail(r => r.Branches = new()), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pattern", await Fail(r => r.Patterns = new() { " " }), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("college name", await Fail(r => r.Name = ""), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Academic year", await Fail(r => r.AcademicYear = null));
        Assert.Contains("at most 2 admins", await Fail(r => r.Admins = new() { Admin("a"), Admin("b"), Admin("c") }));
        Assert.Contains("password", await Fail(r => r.Admins = new() { new AdminInput { Username = "x", Email = "x@t.edu", FirstName = "x", LastName = "x", Password = "short" } }), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Template college not found", await Fail(r => r.TemplateCollegeId = Guid.NewGuid()));
        Assert.Contains("format", await Fail(r => r.Logo = Png("l", type: "application/pdf")));
        Assert.Contains("2MB", await Fail(r => r.Banner = Png("b", bytes: 3 * 1024 * 1024)));

        Assert.Equal(colleges, await rig.Context.Colleges.IgnoreQueryFilters().CountAsync());
        rig.Storage.Verify(s => s.SaveAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Provision_without_a_template_warns_that_results_cannot_be_processed()
    {
        var rig = NewRig();
        var summary = await rig.Provision.ProvisionAsync(Request(rig, r => r.TemplateCollegeId = null));
        Assert.Empty(await rig.Context.GradeMasters.IgnoreQueryFilters().Where(g => g.CollegeId == summary.CollegeId).ToListAsync());
        Assert.Contains(summary.Warnings, w => w.Contains("No template"));
    }

    // ---------------------------------------------------------------- admins (cap, removal)

    [Fact]
    public async Task Third_admin_is_rejected_and_a_removed_admin_frees_the_slot()
    {
        var rig = NewRig();
        var summary = await rig.Provision.ProvisionAsync(Request(rig, r => r.Admins = new() { Admin("one"), Admin("two") }));
        var id = summary.CollegeId;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Colleges.AddAdminAsync(id, Admin("three")));
        Assert.Contains("already has 2 admins", ex.Message);

        // Provisioning a college that is already full is rejected by the same rule.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Provision.ProvisionAsync(Request(rig, r => r.Admins = new() { Admin("three") })));

        // Removing is soft, platform-only, and frees a slot.
        var removed = summary.Admins[0].UserId;
        Assert.True(await rig.Colleges.RemoveAdminAsync(id, removed));
        var gone = await rig.Context.UserMasters.IgnoreQueryFilters().SingleAsync(u => u.UserId == removed);
        Assert.True(gone.IsDeleted);
        var added = await rig.Colleges.AddAdminAsync(id, Admin("three"));
        Assert.Equal("three@test.edu", added.Email);

        var detail = (await rig.Colleges.GetAsync(id))!;
        Assert.Equal(new[] { "three@test.edu", "two@test.edu" }, detail.Admins.Select(a => a.Email).OrderBy(x => x));
    }

    [Fact]
    public async Task Remove_admin_only_touches_an_admin_of_that_college()
    {
        var rig = NewRig();
        var a = await rig.Provision.ProvisionAsync(Request(rig));
        var b = await rig.Provision.ProvisionAsync(Request(rig, r =>
        {
            r.CollegeCode = "TST002"; r.Name = "Second"; r.Admins = new() { Admin("bee") };
        }));

        // The route says college A, the user belongs to B: nothing happens.
        Assert.False(await rig.Colleges.RemoveAdminAsync(a.CollegeId, b.Admins[0].UserId));
        Assert.False((await rig.Context.UserMasters.IgnoreQueryFilters().SingleAsync(u => u.UserId == b.Admins[0].UserId)).IsDeleted);

        // A non-admin user of the college cannot be removed through the admin route either.
        var staffRole = new RoleMaster { RoleId = Guid.NewGuid(), Name = "Teacher", CollegeId = a.CollegeId };
        rig.Context.RoleMasters.Add(staffRole);
        rig.Context.SaveChanges();
        var teacher = await new UserMasterService(rig.Context).CreateUserAsync(new CreateUserMasterDTO
        {
            Username = "t", Email = "t@test.edu", FirstName = "t", LastName = "t", Password = "Password@123", RoleId = staffRole.RoleId,
        }, a.CollegeId, callerIsPlatformAdmin: true);
        Assert.False(await rig.Colleges.RemoveAdminAsync(a.CollegeId, teacher.UserId));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => rig.Colleges.RemoveAdminAsync(Guid.NewGuid(), teacher.UserId));
    }

    // ---------------------------------------------------------------- branches, academic years, list/detail

    [Fact]
    public async Task Branches_and_academic_years_can_be_added_and_current_moves()
    {
        var rig = NewRig();
        var id = (await rig.Provision.ProvisionAsync(Request(rig))).CollegeId;

        var branch = await rig.Colleges.AddBranchAsync(id, new BranchInput { Name = "Second", Code = "SB" });
        Assert.Equal("SB", branch.CourseCode);
        var dup = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Colleges.AddBranchAsync(id, new BranchInput { Name = "x", Code = "sb" }));
        Assert.Contains("already exists", dup.Message);

        var plain = await rig.Colleges.AddAcademicYearAsync(id, new AddAcademicYearRequest { FullDuration = "2025-2026" });
        Assert.False(plain.IsCurrent);                       // not asked, and a current year exists
        var current = await rig.Colleges.AddAcademicYearAsync(id, new AddAcademicYearRequest { FullDuration = "2026-2027", ShortDuration = "26-27", SetCurrent = true });
        Assert.True(current.IsCurrent);
        var years = await rig.Context.AcademicYears.IgnoreQueryFilters().Where(a => a.CollegeId == id).ToListAsync();
        Assert.Single(years.Where(y => y.IsCurrent));        // the previous current was unset
        Assert.Equal("2026-2027", years.Single(y => y.IsCurrent).FullDuration);

        var moved = await rig.Colleges.SetCurrentAcademicYearAsync(id, plain.AYID);
        Assert.True(moved.IsCurrent);
        Assert.Equal(plain.AYID, (await rig.Context.AcademicYears.IgnoreQueryFilters().SingleAsync(a => a.CollegeId == id && a.IsCurrent)).AYID);

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Colleges.AddAcademicYearAsync(id, new AddAcademicYearRequest { FullDuration = "2025-2026" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => rig.Colleges.SetCurrentAcademicYearAsync(id, Guid.NewGuid()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => rig.Colleges.AddBranchAsync(Guid.NewGuid(), new BranchInput { Name = "n", Code = "c" }));
    }

    [Fact]
    public async Task List_and_detail_report_counts_and_flags()
    {
        var rig = NewRig();
        var id = (await rig.Provision.ProvisionAsync(Request(rig, r => { r.Logo = Png("logo"); r.Admins = new() { Admin("one"), Admin("two") }; }))).CollegeId;

        var list = await rig.Colleges.ListAsync();
        Assert.Equal(2, list.Count);                                    // template + new
        var row = list.Single(c => c.CollegeId == id);
        Assert.Equal(("Test College", "TST001", 2, 1, "24-25", true, false),
            (row.Name, row.CollegeCode, row.AdminCount, row.BranchCount, row.CurrentAcademicYear, row.HasLogo, row.HasBanner));
        Assert.Equal(0, list.Single(c => c.CollegeId == rig.TemplateId).AdminCount);

        var detail = (await rig.Colleges.GetAsync(id))!;
        Assert.Single(detail.Branches);
        Assert.Single(detail.Patterns);
        Assert.Single(detail.AcademicYears);
        Assert.Equal(2, detail.Admins.Count);
        Assert.Equal((1, 1), (detail.GradeScaleCount, detail.RuleSetCount));
        Assert.Null(await rig.Colleges.GetAsync(Guid.NewGuid()));
    }

    // ---------------------------------------------------------------- controller

    private static ClaimsPrincipal Principal(string role, Guid? college, bool platform)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new(ClaimTypes.Role, role) };
        if (college != null) claims.Add(new Claim("CollegeId", college.Value.ToString()));
        if (platform) claims.Add(new Claim("IsPlatformAdmin", "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static async Task<bool> Passes(ClaimsPrincipal user, string policy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(o => o.AddAccessPolicies());
        var auth = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
        return (await auth.AuthorizeAsync(user, null, policy)).Succeeded;
    }

    [Fact]
    public async Task A_starter_template_is_flagged_in_the_list_takes_no_admins_and_its_code_cannot_be_provisioned()
    {
        var rig = NewRig();
        var tpl = await rig.Context.Colleges.IgnoreQueryFilters().SingleAsync(c => c.CollegeId == rig.TemplateId);
        tpl.IsTemplate = true;
        await rig.Context.SaveChangesAsync();

        var list = await rig.Colleges.ListAsync();
        Assert.True(list.Single(c => c.CollegeId == rig.TemplateId).IsTemplate);

        var addAdmin = await Assert.ThrowsAsync<ArgumentException>(() => rig.Colleges.AddAdminAsync(rig.TemplateId, Admin("tpladmin")));
        Assert.Contains("template", addAdmin.Message);
        Assert.False(await rig.Context.UserMasters.IgnoreQueryFilters().AnyAsync(u => u.CollegeId == rig.TemplateId));

        var reprovision = await Assert.ThrowsAsync<ArgumentException>(() => rig.Provision.ProvisionAsync(Request(rig, r => { r.CollegeCode = "tpl"; r.TemplateCollegeId = null; })));
        Assert.Contains("template", reprovision.Message);

        // Copying FROM the template still works.
        var summary = await rig.Provision.ProvisionAsync(Request(rig));
        Assert.Equal(1, Created(summary, "Rule sets"));
    }

    [Fact]
    public async Task Every_platform_controller_action_carries_the_PlatformAdmin_policy()
    {
        var actions = typeof(PlatformController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any())
            .ToList();

        Assert.True(actions.Count >= 7, "expected the platform endpoints to be discovered");
        foreach (var m in actions)
        {
            Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>());
            var policy = m.GetCustomAttribute<AuthorizeAttribute>()?.Policy
                         ?? typeof(PlatformController).GetCustomAttribute<AuthorizeAttribute>()?.Policy;
            Assert.True(policy == AccessPolicies.PlatformAdmin, $"{m.Name} must require PlatformAdmin but requires '{policy}'");
        }

        // The policy lets the platform admin in and keeps out a college admin and plain staff (403).
        Assert.True(await Passes(Principal("PlatformAdmin", null, platform: true), AccessPolicies.PlatformAdmin));
        Assert.False(await Passes(Principal("Admin", Guid.NewGuid(), platform: false), AccessPolicies.PlatformAdmin));
        Assert.False(await Passes(Principal("Teacher", Guid.NewGuid(), platform: false), AccessPolicies.PlatformAdmin));
    }

    [Fact]
    public async Task Controller_maps_rule_violations_to_400_and_unknown_colleges_to_404()
    {
        var rig = NewRig();
        var controller = new PlatformController(rig.Provision, rig.Colleges);

        var ok = Assert.IsType<OkObjectResult>(await controller.ProvisionCollege(Request(rig, r => r.Admins = new() { Admin("one"), Admin("two") }), default));
        var summary = Assert.IsType<ProvisionSummary>(ok.Value);

        var third = Assert.IsType<BadRequestObjectResult>(await controller.AddAdmin(summary.CollegeId, Admin("three"), default));
        Assert.Contains("already has 2 admins", third.Value!.ToString());

        var invalid = Assert.IsType<BadRequestObjectResult>(await controller.ProvisionCollege(Request(rig, r => r.Branches = new()), default));
        Assert.Contains("branch", invalid.Value!.ToString(), StringComparison.OrdinalIgnoreCase);

        Assert.IsType<NotFoundObjectResult>(await controller.GetCollege(Guid.NewGuid(), default));
        Assert.IsType<NotFoundObjectResult>(await controller.AddBranch(Guid.NewGuid(), new BranchInput { Name = "n", Code = "c" }, default));
        Assert.IsType<NotFoundObjectResult>(await controller.RemoveAdmin(summary.CollegeId, Guid.NewGuid(), default));
        Assert.IsType<OkObjectResult>(await controller.GetColleges(default));
    }
}
