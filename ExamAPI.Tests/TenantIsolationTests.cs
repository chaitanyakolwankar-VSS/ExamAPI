using System.Security.Claims;
using ExamAPI.Controllers;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Ordinance;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.RoleMaster;
using ExamAPI.Services.StudentMasters;
using ExamAPI.Services.Files;
using ExamAPI.Services.Subject;
using ExamAPI.Services.Tenancy;
using ExamAPI.Services.UsersMaster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>
/// Tenant isolation and authorization gaps found by the audit: shared template rows (CollegeId == null) must not be
/// changed by a college, rules/credits/subjects/marks are reached only through the caller's own college, and a user's
/// details / password are reachable only by that user or an admin.
/// </summary>
public sealed class TenantIsolationTests
{
    private static readonly Guid CollegeA = Guid.NewGuid();
    private static readonly Guid CollegeB = Guid.NewGuid();

    private readonly DbContextOptions<ApplicationDbContext> _options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;

    // seeded ids
    private readonly Guid _templateGrade = Guid.NewGuid(), _gradeA = Guid.NewGuid(), _gradeB = Guid.NewGuid();
    private readonly Guid _templateRuleSet = Guid.NewGuid(), _ruleSetA = Guid.NewGuid(), _ruleSetB = Guid.NewGuid();
    private readonly Guid _templateRule = Guid.NewGuid(), _ruleA = Guid.NewGuid(), _ruleB = Guid.NewGuid();
    private readonly Guid _templateRole = Guid.NewGuid(), _roleA = Guid.NewGuid();
    private readonly Guid _marksA = Guid.NewGuid(), _marksB = Guid.NewGuid();
    private readonly Guid _subjectA = Guid.NewGuid(), _subjectB = Guid.NewGuid();
    private readonly Guid _creditsB = Guid.NewGuid(), _creditHeadB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid(), _colleagueA = Guid.NewGuid();

    private ApplicationDbContext Ctx(Guid? college, bool platform = false, Guid? userId = null)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.CollegeId).Returns(college);
        user.SetupGet(u => u.IsPlatformAdmin).Returns(platform);
        user.SetupGet(u => u.UserId).Returns(userId);
        return new ApplicationDbContext(_options, new Mock<IHttpContextAccessor>().Object, user.Object);
    }

    public TenantIsolationTests()
    {
        // The platform view has no CollegeId, so explicit CollegeIds on the rows below are kept as written.
        using var c = Ctx(null, platform: true);
        c.GradeMasters.AddRange(
            new GradeMaster { GradeMasterId = _templateGrade, Name = "Template", CollegeId = null },
            new GradeMaster { GradeMasterId = _gradeA, Name = "A grades", CollegeId = CollegeA },
            new GradeMaster { GradeMasterId = _gradeB, Name = "B grades", CollegeId = CollegeB });
        c.RuleSets.AddRange(
            new RuleSet { RuleSetId = _templateRuleSet, Name = "Template RS", CollegeId = null, PatternId = Guid.NewGuid() },
            new RuleSet { RuleSetId = _ruleSetA, Name = "A RS", CollegeId = CollegeA, PatternId = Guid.NewGuid() },
            new RuleSet { RuleSetId = _ruleSetB, Name = "B RS", CollegeId = CollegeB, PatternId = Guid.NewGuid() });
        c.Rules.AddRange(
            new Rule { RuleId = _templateRule, Name = "Template rule", RuleSetId = _templateRuleSet },
            new Rule { RuleId = _ruleA, Name = "A rule", RuleSetId = _ruleSetA },
            new Rule { RuleId = _ruleB, Name = "B rule", RuleSetId = _ruleSetB });
        c.RoleMasters.AddRange(
            new RoleMaster { RoleId = _templateRole, Name = "Clerk template", CollegeId = null },
            new RoleMaster { RoleId = _roleA, Name = "Clerk", CollegeId = CollegeA });
        c.MarksMasters.AddRange(
            new MarksMaster { MarksId = _marksA, StudentID = "2600001", CollegeId = CollegeA },
            new MarksMaster { MarksId = _marksB, StudentID = "2600002", CollegeId = CollegeB });
        c.StudentMarks.AddRange(
            new StudentMarks { Id = Guid.NewGuid(), MarksId = _marksA, Head = "H1" },
            new StudentMarks { Id = Guid.NewGuid(), MarksId = _marksB, Head = "H1" });
        c.SubjectMasters.AddRange(
            new SubjectMaster { SubjectId = _subjectA, SubjectCode = "A1", Name = "A subject", CollegeId = CollegeA },
            new SubjectMaster { SubjectId = _subjectB, SubjectCode = "B1", Name = "B subject", CollegeId = CollegeB });
        c.SubjectCreditMasters.Add(new SubjectCreditMaster { CreditsId = _creditsB, SubjectId = _subjectB, TotalCredits = "2", AYID = "ay", CollegeId = CollegeB });
        c.SubjectCredits.Add(new SubjectCredits { Id = _creditHeadB, CreditsId = _creditsB, Head = "H1", HeadType = "ESE", HeadOutOf = "60", HeadPass = "24" });
        c.UserMasters.AddRange(
            new UserMaster { UserId = _userA, Username = "a1", HashedPassword = BCrypt.Net.BCrypt.HashPassword("Old@1234"), Email = "a1@a.in", FirstName = "A", LastName = "One", CollegeId = CollegeA },
            new UserMaster { UserId = _colleagueA, Username = "a2", HashedPassword = BCrypt.Net.BCrypt.HashPassword("Old@1234"), Email = "a2@a.in", FirstName = "A", LastName = "Two", CollegeId = CollegeA });
        c.SaveChanges();
    }

    private OrdinanceService Ordinance(ApplicationDbContext ctx) =>
        new(ctx, new EngineRegistry(Array.Empty<IFactProvider>(), Array.Empty<IActionHandler>()));

    // ---------- 1. Template grade masters / rule sets ----------

    [Fact]
    public async Task College_user_cannot_update_or_delete_a_template_grade_master()
    {
        var service = Ordinance(Ctx(CollegeA));

        var update = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateGradeMasterAsync(
            new GradeMasterUpdateDto { GradeMasterId = _templateGrade, Name = "Hacked", Description = "", Thresholds = new() }));
        Assert.Contains("shared template", update.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteGradeMasterAsync(_templateGrade));

        using var check = Ctx(null, platform: true);
        var row = await check.GradeMasters.SingleAsync(g => g.GradeMasterId == _templateGrade);
        Assert.Equal("Template", row.Name);
        Assert.False(row.IsDeleted);
    }

    [Fact]
    public async Task College_user_can_still_change_their_own_grade_master_and_platform_admin_can_change_the_template()
    {
        Assert.True(await Ordinance(Ctx(CollegeA)).UpdateGradeMasterAsync(
            new GradeMasterUpdateDto { GradeMasterId = _gradeA, Name = "A renamed", Description = "", Thresholds = new() }));
        // Another college's grade master is simply not found for this caller.
        Assert.False(await Ordinance(Ctx(CollegeA)).UpdateGradeMasterAsync(
            new GradeMasterUpdateDto { GradeMasterId = _gradeB, Name = "x", Description = "", Thresholds = new() }));

        Assert.True(await Ordinance(Ctx(null, platform: true)).UpdateGradeMasterAsync(
            new GradeMasterUpdateDto { GradeMasterId = _templateGrade, Name = "Template v2", Description = "", Thresholds = new() }));
        Assert.True(await Ordinance(Ctx(null, platform: true)).DeleteGradeMasterAsync(_templateGrade));

        using var check = Ctx(null, platform: true);
        Assert.Equal("A renamed", (await check.GradeMasters.IgnoreQueryFilters().SingleAsync(g => g.GradeMasterId == _gradeA)).Name);
        Assert.Equal("B grades", (await check.GradeMasters.IgnoreQueryFilters().SingleAsync(g => g.GradeMasterId == _gradeB)).Name);
        Assert.True((await check.GradeMasters.IgnoreQueryFilters().SingleAsync(g => g.GradeMasterId == _templateGrade)).IsDeleted);
    }

    [Fact]
    public async Task College_user_cannot_update_or_delete_a_template_rule_set_but_platform_admin_can()
    {
        var service = Ordinance(Ctx(CollegeA));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateRuleSetAsync(
            new RuleSetUpdateDto { RuleSetId = _templateRuleSet, Name = "Hacked", IsActive = true }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteRuleSetAsync(_templateRuleSet));

        using (var check = Ctx(null, platform: true))
        {
            var row = await check.RuleSets.SingleAsync(r => r.RuleSetId == _templateRuleSet);
            Assert.Equal("Template RS", row.Name);
            Assert.False(row.IsDeleted);
        }

        Assert.True(await Ordinance(Ctx(null, platform: true)).UpdateRuleSetAsync(
            new RuleSetUpdateDto { RuleSetId = _templateRuleSet, Name = "Template RS v2", IsActive = true }));
        Assert.True(await Ordinance(Ctx(null, platform: true)).DeleteRuleSetAsync(_templateRuleSet));
    }

    // ---------- 1. Rules ----------

    [Fact]
    public async Task Rules_of_another_colleges_rule_set_are_not_listed_but_a_template_rule_set_is_readable()
    {
        var service = Ordinance(Ctx(CollegeA));

        Assert.Empty(await service.GetRulesByRuleSetAsync(_ruleSetB));
        Assert.Single(await service.GetRulesByRuleSetAsync(_templateRuleSet));
        Assert.Single(await service.GetRulesByRuleSetAsync(_ruleSetA));
    }

    [Fact]
    public async Task College_user_cannot_create_rules_in_another_colleges_or_a_template_rule_set()
    {
        var service = Ordinance(Ctx(CollegeA));

        var other = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateRuleAsync(new RuleCreateDto { RuleSetId = _ruleSetB, Name = "Planted" }));
        Assert.Contains("not found", other.Message);
        var template = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateRuleAsync(new RuleCreateDto { RuleSetId = _templateRuleSet, Name = "Planted" }));
        Assert.Contains("shared template", template.Message);

        using (var check = Ctx(null, platform: true))
            Assert.DoesNotContain(await check.Rules.ToListAsync(), r => r.Name == "Planted");

        // Own rule set works; the platform admin may add to the template.
        var own = await service.CreateRuleAsync(new RuleCreateDto { RuleSetId = _ruleSetA, Name = "Mine" });
        Assert.Equal(_ruleSetA, own.RuleSetId);
        var platform = await Ordinance(Ctx(null, platform: true)).CreateRuleAsync(new RuleCreateDto { RuleSetId = _templateRuleSet, Name = "Platform rule" });
        Assert.Equal(_templateRuleSet, platform.RuleSetId);
    }

    [Fact]
    public async Task College_user_cannot_update_or_delete_rules_of_another_college_or_of_a_template()
    {
        var service = Ordinance(Ctx(CollegeA));

        // Another college's rule: not found (the rule set is invisible), nothing changes.
        Assert.False(await service.UpdateRuleAsync(new RuleUpdateDto { RuleId = _ruleB, Name = "Hacked", Conditions = new(), Actions = new() }));
        Assert.False(await service.DeleteRuleAsync(_ruleB));

        // A template rule: refused with the template sentence.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateRuleAsync(new RuleUpdateDto { RuleId = _templateRule, Name = "Hacked", Conditions = new(), Actions = new() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteRuleAsync(_templateRule));

        using (var check = Ctx(null, platform: true))
        {
            var rules = await check.Rules.IgnoreQueryFilters().ToListAsync();
            Assert.Equal("B rule", rules.Single(r => r.RuleId == _ruleB).Name);
            Assert.Equal("Template rule", rules.Single(r => r.RuleId == _templateRule).Name);
            Assert.All(rules, r => Assert.False(r.IsDeleted));
        }

        // Own rule and (platform admin) the template rule can be changed.
        Assert.True(await service.UpdateRuleAsync(new RuleUpdateDto { RuleId = _ruleA, Name = "A edited", Conditions = new(), Actions = new() }));
        Assert.True(await Ordinance(Ctx(null, platform: true)).UpdateRuleAsync(new RuleUpdateDto { RuleId = _templateRule, Name = "Template edited", Conditions = new(), Actions = new() }));
        Assert.True(await Ordinance(Ctx(null, platform: true)).DeleteRuleAsync(_templateRule));
    }

    [Fact]
    public async Task Ordinance_controller_turns_the_template_refusal_into_400_with_the_message()
    {
        var controller = new OrdinanceController(Ordinance(Ctx(CollegeA)));

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.UpdateRuleSet(_templateRuleSet,
            new RuleSetUpdateDto { RuleSetId = _templateRuleSet, Name = "Hacked", IsActive = true }));
        Assert.Contains("shared template", Assert.IsType<ApiResponseDto<object>>(bad.Value).Message);
        Assert.IsType<BadRequestObjectResult>(await controller.DeleteGradeMaster(_templateGrade));
        Assert.IsType<BadRequestObjectResult>(await controller.DeleteRule(_templateRule));
        Assert.IsType<NotFoundObjectResult>(await controller.DeleteRule(_ruleB));
    }

    // ---------- 1. Template roles ----------

    [Fact]
    public async Task A_rule_set_cannot_be_linked_to_another_colleges_grade_master()
    {
        using var ctx = Ctx(CollegeA);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Ordinance(ctx).UpdateRuleSetAsync(
            new RuleSetUpdateDto { RuleSetId = _ruleSetA, Name = "A RS", GradeMasterId = _gradeB }));
        Assert.Equal("Grade master not found.", refused.Message);
        Assert.True(await Ordinance(ctx).UpdateRuleSetAsync(new RuleSetUpdateDto { RuleSetId = _ruleSetA, Name = "A RS", GradeMasterId = _templateGrade }));
        Assert.True(await Ordinance(ctx).UpdateRuleSetAsync(new RuleSetUpdateDto { RuleSetId = _ruleSetA, Name = "A RS", GradeMasterId = _gradeA }));
    }

    private RoleMasterService Roles(Guid? college, bool platform)
    {
        var cu = new Mock<ICurrentUser>();
        cu.SetupGet(u => u.CollegeId).Returns(college);
        cu.SetupGet(u => u.IsPlatformAdmin).Returns(platform);
        return new RoleMasterService(Ctx(college, platform), cu.Object);
    }

    [Fact]
    public async Task College_admin_cannot_update_or_delete_a_template_role_but_platform_admin_can()
    {
        var roles = Roles(CollegeA, platform: false);

        var update = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            roles.UpdateRoleAsync(new CreateRoleDto { RoleId = _templateRole, Name = "Hacked" }));
        Assert.Contains("shared template", update.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => roles.DeleteRoleAsync(_templateRole));

        using (var check = Ctx(null, platform: true))
        {
            var role = await check.RoleMasters.SingleAsync(r => r.RoleId == _templateRole);
            Assert.Equal("Clerk template", role.Name);
            Assert.False(role.IsDeleted);
        }

        // Their own role is fine.
        Assert.Equal("Role updated successfully", await roles.UpdateRoleAsync(new CreateRoleDto { RoleId = _roleA, Name = "Senior Clerk" }));

        var platform = Roles(null, platform: true);
        Assert.Equal("Role updated successfully", await platform.UpdateRoleAsync(new CreateRoleDto { RoleId = _templateRole, Name = "Clerk template v2" }));
        Assert.Equal("Role deleted successfully", await platform.DeleteRoleAsync(_templateRole));
    }

    // ---------- 2. StudentMaster.DeleteExam ----------

    [Fact]
    public async Task DeleteExam_with_another_colleges_marks_id_leaves_its_marks_untouched()
    {
        var service = new StudentMasterService(Ctx(CollegeA), new Mock<IFileStorage>().Object);

        // College A names its own student id but college B's MarksId.
        await service.DeleteExamAsync("2600002", _marksB);
        await service.DeleteExamAsync("2600001", _marksB);

        using (var check = Ctx(null, platform: true))
        {
            Assert.False((await check.MarksMasters.IgnoreQueryFilters().SingleAsync(m => m.MarksId == _marksB)).IsDeleted);
            Assert.All(await check.StudentMarks.IgnoreQueryFilters().Where(s => s.MarksId == _marksB).ToListAsync(), s => Assert.False(s.IsDeleted));
        }

        // Its own exam still deletes (master and marks).
        Assert.Equal("Deleted successfully", await service.DeleteExamAsync("2600001", _marksA));
        using var after = Ctx(null, platform: true);
        Assert.True((await after.MarksMasters.IgnoreQueryFilters().SingleAsync(m => m.MarksId == _marksA)).IsDeleted);
        Assert.All(await after.StudentMarks.IgnoreQueryFilters().Where(s => s.MarksId == _marksA).ToListAsync(), s => Assert.True(s.IsDeleted));
    }

    // ---------- 3. Subject credits ----------

    private SubjectService Subjects(Guid college)
    {
        var ctx = Ctx(college);
        return new SubjectService(ctx, new GenericRepository(ctx));
    }

    [Fact]
    public async Task UpdateCredit_with_another_colleges_credit_id_is_refused_and_changes_nothing()
    {
        var result = await Subjects(CollegeA).UpdateCreditAsync(new SaveCreditsDto
        {
            SubjectId = _subjectA,
            Ayid = "ay",
            Credits = new List<CreditDto>
            {
                new()
                {
                    CreditId = _creditsB,
                    CreditNo = "9",
                    ExamType = new List<string> { "HACKED" },
                    InternalType = new List<string> { "IA" },
                    ExamOutOf = "1", ExamPassing = "1", InternalOutOf = "1", InternalPassing = "1",
                    PassingStrategy = "HeadWise",
                },
            },
        });

        Assert.False(result.Success);
        Assert.Equal("Credits not found.", result.Message);

        using var check = Ctx(null, platform: true);
        Assert.Equal("2", (await check.SubjectCreditMasters.IgnoreQueryFilters().SingleAsync(c => c.CreditsId == _creditsB)).TotalCredits);
        var heads = await check.SubjectCredits.IgnoreQueryFilters().Where(c => c.CreditsId == _creditsB).ToListAsync();
        var head = Assert.Single(heads);
        Assert.Equal("ESE", head.HeadType);
        Assert.False(head.IsDeleted);
    }

    // ---------- 4. Subject delete / GenericRepository ----------

    [Fact]
    public async Task DeleteSubject_with_another_colleges_subject_id_is_refused_and_leaves_it_undeleted()
    {
        var result = await Subjects(CollegeA).DeleteSubjectAsync(new DeleteSubjectDto { SubjectId = _subjectB });

        Assert.False(result.Success);
        Assert.Equal("Subject not found.", result.Message);

        using var check = Ctx(null, platform: true);
        Assert.False((await check.SubjectMasters.IgnoreQueryFilters().SingleAsync(s => s.SubjectId == _subjectB)).IsDeleted);
        Assert.False((await check.SubjectCreditMasters.IgnoreQueryFilters().SingleAsync(c => c.CreditsId == _creditsB)).IsDeleted);

        // Own subject deletes.
        Assert.True((await Subjects(CollegeA).DeleteSubjectAsync(new DeleteSubjectDto { SubjectId = _subjectA })).Success);
    }

    [Fact]
    public async Task GenericRepository_delete_respects_the_tenant_filter()
    {
        var ctx = Ctx(CollegeA);
        var repo = new GenericRepository(ctx);

        await repo.DeleteAsync<SubjectMaster>(_subjectB);   // another college's row: a no-op
        await repo.DeleteAsync<SubjectMaster>(_subjectA);   // own row: soft-deleted

        using var check = Ctx(null, platform: true);
        Assert.False((await check.SubjectMasters.IgnoreQueryFilters().SingleAsync(s => s.SubjectId == _subjectB)).IsDeleted);
        Assert.True((await check.SubjectMasters.IgnoreQueryFilters().SingleAsync(s => s.SubjectId == _subjectA)).IsDeleted);
    }

    // ---------- 5. UserMaster: ChangePassword and GetAll/{id} ----------

    private static ClaimsPrincipal Principal(Guid userId, string role, Guid? college, bool platform = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role),
        };
        if (college != null) claims.Add(new Claim("CollegeId", college.Value.ToString()));
        if (platform) claims.Add(new Claim("IsPlatformAdmin", "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private UserMasterController UserController(ClaimsPrincipal user) =>
        new(new UserMasterService(Ctx(CollegeA))) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } } };

    private static void AssertForbidden(IActionResult result) =>
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);

    private async Task<bool> PasswordIs(Guid userId, string password)
    {
        using var check = Ctx(null, platform: true);
        var user = await check.UserMasters.IgnoreQueryFilters().SingleAsync(u => u.UserId == userId);
        return BCrypt.Net.BCrypt.Verify(password, user.HashedPassword);
    }

    [Fact]
    public async Task ChangePassword_for_another_user_by_a_non_admin_is_refused_while_self_and_admin_succeed()
    {
        var staff = UserController(Principal(_userA, "Teacher", CollegeA));

        // A teacher cannot try (or set) a colleague's password.
        AssertForbidden(await staff.ChangePassword(new ChangePasswordDTO { UserId = _colleagueA, CurrentPassword = "Old@1234", NewPassword = "New@1234" }));
        Assert.True(await PasswordIs(_colleagueA, "Old@1234"));

        // Self-service still works.
        Assert.IsType<OkObjectResult>(await staff.ChangePassword(new ChangePasswordDTO { UserId = _userA, CurrentPassword = "Old@1234", NewPassword = "Self@1234" }));
        Assert.True(await PasswordIs(_userA, "Self@1234"));

        // A college admin may change a colleague's password (ResetPasswordModal from the Create User screen).
        var admin = UserController(Principal(Guid.NewGuid(), "Admin", CollegeA));
        Assert.IsType<OkObjectResult>(await admin.ChangePassword(new ChangePasswordDTO { UserId = _colleagueA, CurrentPassword = "Old@1234", NewPassword = "Admin@1234" }));
        Assert.True(await PasswordIs(_colleagueA, "Admin@1234"));
    }

    [Fact]
    public async Task ChangePassword_is_rate_limited_like_sign_in()
    {
        var attribute = typeof(UserMasterController).GetMethod(nameof(UserMasterController.ChangePassword))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>().SingleOrDefault();
        Assert.NotNull(attribute);
        Assert.Equal(ExamAPI.Services.Auth.RateLimits.SignIn, attribute!.PolicyName);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetAll_by_id_for_another_user_by_a_non_admin_is_refused_while_self_and_admin_can_read()
    {
        var staff = UserController(Principal(_userA, "Teacher", CollegeA));

        AssertForbidden(await staff.GetById(_colleagueA));
        Assert.IsType<OkObjectResult>(await staff.GetById(_userA));

        var admin = UserController(Principal(Guid.NewGuid(), "Admin", CollegeA));
        Assert.IsType<OkObjectResult>(await admin.GetById(_colleagueA));
    }
}
