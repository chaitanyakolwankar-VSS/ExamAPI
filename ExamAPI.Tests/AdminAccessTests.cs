using System.Reflection;
using System.Security.Claims;
using ExamAPI.Controllers;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Auth;
using ExamAPI.Services.CollegeDetail;
using ExamAPI.Services.Files;
using ExamAPI.Services.RoleMaster;
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
/// DEC-17 / DB-05 access model: platform admin creates colleges and college admins (max 2 per
/// college); only the college admin reaches the Admin screens. There is no WebApplicationFactory in
/// this project, so "403" is verified as: the endpoint carries the right [Authorize(Policy)] (reflection)
/// and that policy DENIES an authenticated non-admin (MVC answers an authenticated-but-denied request 403).
/// </summary>
public sealed class AdminAccessTests
{
    private static readonly Guid CollegeA = Guid.NewGuid();
    private static readonly Guid CollegeB = Guid.NewGuid();

    // ---------- principals ----------

    private static ClaimsPrincipal Principal(string role, Guid? college, bool platform = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, "tester"),
            new(ClaimTypes.Role, role),
        };
        if (college != null) claims.Add(new Claim("CollegeId", college.Value.ToString()));
        if (platform) claims.Add(new Claim("IsPlatformAdmin", "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal Staff() => Principal("Teacher", CollegeA);
    private static ClaimsPrincipal CollegeAdminUser(string role = "Admin") => Principal(role, CollegeA);
    private static ClaimsPrincipal PlatformAdminUser() => Principal("PlatformAdmin", null, platform: true);
    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static IAuthorizationService AuthService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(o => o.AddAccessPolicies());
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static async Task<bool> Passes(ClaimsPrincipal user, string policy) =>
        (await AuthService().AuthorizeAsync(user, null, policy)).Succeeded;

    // ---------- policy behaviour ----------

    [Fact]
    public async Task Non_admin_staff_fail_both_policies()
    {
        Assert.False(await Passes(Staff(), AccessPolicies.CollegeAdmin));
        Assert.False(await Passes(Staff(), AccessPolicies.PlatformAdmin));
        Assert.False(await Passes(Anonymous(), AccessPolicies.CollegeAdmin));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData(" Admin ")]
    public async Task College_admin_role_matches_case_insensitively_and_passes_only_the_college_policy(string role)
    {
        var admin = CollegeAdminUser(role);
        Assert.True(await Passes(admin, AccessPolicies.CollegeAdmin));
        Assert.False(await Passes(admin, AccessPolicies.PlatformAdmin));   // a college admin can NOT create a college
    }

    [Fact]
    public async Task Role_names_that_merely_contain_admin_are_not_admins()
    {
        Assert.False(await Passes(Principal("Administrator", CollegeA), AccessPolicies.CollegeAdmin));
        Assert.False(await Passes(Principal("SubAdmin", CollegeA), AccessPolicies.CollegeAdmin));
    }

    [Fact]
    public async Task Platform_admin_passes_both_policies()
    {
        Assert.True(await Passes(PlatformAdminUser(), AccessPolicies.PlatformAdmin));
        Assert.True(await Passes(PlatformAdminUser(), AccessPolicies.CollegeAdmin));
    }

    [Fact]
    public async Task A_role_claim_alone_does_not_make_a_platform_admin()
    {
        // The role is just a name; only the IsPlatformAdmin claim (set from UserMaster.IsPlatformAdmin) counts.
        Assert.False(await Passes(Principal("PlatformAdmin", CollegeA), AccessPolicies.PlatformAdmin));
    }

    // ---------- endpoint -> policy table (non-admin gets 403 on each) ----------

    public static IEnumerable<object[]> AdminEndpoints()
    {
        // college-admin endpoints
        foreach (var m in new[] { "CreateUser", "GetAllUsers", "DeleteUser", "UpdateUser" })
            yield return new object[] { typeof(UserMasterController), m, AccessPolicies.CollegeAdmin };
        foreach (var m in new[] { "GetInfo", "Selectmodule", "GetRoleById", "SaveRole", "UpdateRole", "DeleteRole" })
            yield return new object[] { typeof(RoleMasterController), m, AccessPolicies.CollegeAdmin };
        foreach (var m in new[] { "Create", "GetModules", "GetGroupedPermissions", "Update", "Delete" })
            yield return new object[] { typeof(PermissionController), m, AccessPolicies.CollegeAdmin };
        yield return new object[] { typeof(CollegeDetailController), "Update", AccessPolicies.CollegeAdmin };
        foreach (var m in new[] { "DeleteStudent", "RestoreExam", "DeleteExam" })
            yield return new object[] { typeof(StudentMasterController), m, AccessPolicies.CollegeAdmin };

        // platform-admin-only endpoint
        yield return new object[] { typeof(CollegeDetailController), "Create", AccessPolicies.PlatformAdmin };
    }

    private static string? EffectivePolicy(Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     ?? throw new InvalidOperationException($"{controller.Name}.{action} not found");
        if (method.GetCustomAttribute<AllowAnonymousAttribute>() != null) return null;
        var attr = method.GetCustomAttribute<AuthorizeAttribute>() ?? controller.GetCustomAttribute<AuthorizeAttribute>();
        return attr?.Policy;
    }

    [Theory]
    [MemberData(nameof(AdminEndpoints))]
    public async Task Every_admin_endpoint_carries_its_policy_and_denies_a_non_admin_with_403(Type controller, string action, string policy)
    {
        var effective = EffectivePolicy(controller, action);
        Assert.Equal(policy, effective);

        // Authenticated non-admin -> policy fails -> MVC returns 403 Forbidden (anonymous would be 401).
        var staff = Staff();
        Assert.True(staff.Identity!.IsAuthenticated);
        Assert.False(await Passes(staff, effective!));
        if (policy == AccessPolicies.PlatformAdmin)
            Assert.False(await Passes(CollegeAdminUser(), policy)); // not even a college admin
    }

    [Theory]
    [InlineData(typeof(UserMasterController), "GetById")]        // GetAll/{id}: reset-password modal reads the caller's own user
    [InlineData(typeof(UserMasterController), "ChangePassword")] // self-service, guarded by the current password
    [InlineData(typeof(CollegeDetailController), "Get")]         // own college name/branding
    [InlineData(typeof(StudentMasterController), "GetData")]
    [InlineData(typeof(StudentMasterController), "SaveStudent")]
    [InlineData(typeof(StudentMasterController), "UpdateStudent")]
    public void Endpoints_used_by_normal_staff_screens_are_not_admin_locked(Type controller, string action)
    {
        var policy = EffectivePolicy(controller, action);
        Assert.True(policy is null, $"{controller.Name}.{action} must stay open to non-admin staff but requires '{policy}'");
    }

    // ---------- 2-admin cap + platform-admin-only admin creation (service + controller) ----------

    private sealed class Rig
    {
        public required ApplicationDbContext Context { get; init; }
        public required DbContextOptions<ApplicationDbContext> Options { get; init; }

        /// <summary>A context on the same database, scoped like a given caller (null college = platform).</summary>
        public ApplicationDbContext ScopedTo(Guid? college, bool platform)
        {
            var cu = new Mock<ICurrentUser>();
            cu.SetupGet(u => u.CollegeId).Returns(college);
            cu.SetupGet(u => u.IsPlatformAdmin).Returns(platform);
            return new ApplicationDbContext(Options, new Mock<IHttpContextAccessor>().Object, cu.Object);
        }
        public required UserMasterService Service { get; init; }
        public Guid AdminRoleA { get; init; }
        public Guid StaffRoleA { get; init; }
        public Guid AdminRoleB { get; init; }
    }

    private static Rig NewRig()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.IsPlatformAdmin).Returns(true); // no CollegeId: the platform view
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ctx = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);

        ctx.Colleges.Add(new College { CollegeId = CollegeA, Name = "A", CollegeCode = "A", CollegeCenter = "A", ContactEmail = "a@a", ContactPhone = "1" });
        ctx.Colleges.Add(new College { CollegeId = CollegeB, Name = "B", CollegeCode = "B", CollegeCenter = "B", ContactEmail = "b@b", ContactPhone = "1" });
        var adminA = new RoleMaster { RoleId = Guid.NewGuid(), Name = "Admin", CollegeId = CollegeA };
        var staffA = new RoleMaster { RoleId = Guid.NewGuid(), Name = "Teacher", CollegeId = CollegeA };
        var adminB = new RoleMaster { RoleId = Guid.NewGuid(), Name = "ADMIN", CollegeId = CollegeB };
        ctx.RoleMasters.AddRange(adminA, staffA, adminB);
        ctx.SaveChanges();

        return new Rig
        {
            Context = ctx,
            Options = options,
            Service = new UserMasterService(ctx),
            AdminRoleA = adminA.RoleId,
            StaffRoleA = staffA.RoleId,
            AdminRoleB = adminB.RoleId,
        };
    }

    private static CreateUserMasterDTO NewUser(string name, Guid? roleId) => new()
    {
        Username = name,
        Password = "Password@123",
        Email = $"{name}@example.com",
        FirstName = name,
        LastName = "T",
        RoleId = roleId,
    };

    [Fact]
    public async Task Platform_admin_can_create_two_admins_and_the_third_is_rejected()
    {
        var rig = NewRig();

        await rig.Service.CreateUserAsync(NewUser("admin1", rig.AdminRoleA), CollegeA, callerIsPlatformAdmin: true);
        await rig.Service.CreateUserAsync(NewUser("admin2", rig.AdminRoleA), CollegeA, callerIsPlatformAdmin: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Service.CreateUserAsync(NewUser("admin3", rig.AdminRoleA), CollegeA, callerIsPlatformAdmin: true));
        Assert.Contains("already has 2 admins", ex.Message);
    }

    [Fact]
    public async Task Admin_cap_is_per_college_and_a_deleted_admin_frees_a_slot()
    {
        var rig = NewRig();
        var first = await rig.Service.CreateUserAsync(NewUser("a1", rig.AdminRoleA), CollegeA, true);
        await rig.Service.CreateUserAsync(NewUser("a2", rig.AdminRoleA), CollegeA, true);

        // Another college is unaffected by A being full.
        await rig.Service.CreateUserAsync(NewUser("b1", rig.AdminRoleB), CollegeB, true);

        await rig.Service.DeleteUserById(first.UserId, callerIsPlatformAdmin: true);
        await rig.Service.CreateUserAsync(NewUser("a3", rig.AdminRoleA), CollegeA, true);
    }

    [Fact]
    public async Task College_admin_cannot_create_an_admin_but_can_create_ordinary_users()
    {
        var rig = NewRig();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Service.CreateUserAsync(NewUser("sneaky", rig.AdminRoleA), CollegeA, callerIsPlatformAdmin: false));
        Assert.Contains("platform administrator", ex.Message);

        var staff = await rig.Service.CreateUserAsync(NewUser("teacher1", rig.StaffRoleA), CollegeA, callerIsPlatformAdmin: false);
        Assert.Equal("teacher1", staff.Username);
    }

    [Fact]
    public async Task A_role_from_another_college_is_rejected()
    {
        var rig = NewRig();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Service.CreateUserAsync(NewUser("x", rig.AdminRoleB), CollegeA, callerIsPlatformAdmin: true));
        Assert.Contains("Role not found", ex.Message);
    }

    [Fact]
    public async Task Promoting_a_user_to_admin_needs_the_platform_admin_and_respects_the_cap()
    {
        var rig = NewRig();
        await rig.Service.CreateUserAsync(NewUser("a1", rig.AdminRoleA), CollegeA, true);
        await rig.Service.CreateUserAsync(NewUser("a2", rig.AdminRoleA), CollegeA, true);
        var teacher = await rig.Service.CreateUserAsync(NewUser("t1", rig.StaffRoleA), CollegeA, false);

        UpdateUserMasterDTO Promote() => new()
        {
            UserId = teacher.UserId, Username = "t1", FirstName = "t1", LastName = "T", Email = "t1@example.com", RoleId = rig.AdminRoleA,
        };

        // The college admin's context is scoped to college A, where the user is visible.
        var asCollegeAdmin = new UserMasterService(rig.ScopedTo(CollegeA, platform: false));
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => asCollegeAdmin.UpdateUserMaster(Promote(), callerIsPlatformAdmin: false));
        Assert.Contains("platform administrator", denied.Message);

        var capped = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Service.UpdateUserMaster(Promote(), callerIsPlatformAdmin: true));
        Assert.Contains("already has 2 admins", capped.Message);
    }

    [Fact]
    public async Task College_admin_cannot_delete_an_admin_user()
    {
        var rig = NewRig();
        var admin = await rig.Service.CreateUserAsync(NewUser("a1", rig.AdminRoleA), CollegeA, true);

        // As the college admin: the context is scoped to college A, where the admin user is visible.
        var asCollegeAdmin = new UserMasterService(rig.ScopedTo(CollegeA, platform: false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => asCollegeAdmin.DeleteUserById(admin.UserId, callerIsPlatformAdmin: false));

        // The platform admin may remove it.
        Assert.True(await rig.Service.DeleteUserById(admin.UserId, callerIsPlatformAdmin: true));
    }

    // ---------- controller: 400 + clear message; CollegeId handling ----------

    private static UserMasterController ControllerFor(Rig rig, ClaimsPrincipal user) =>
        new(rig.Service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } } };

    [Fact]
    public async Task Controller_returns_400_with_message_for_the_third_admin()
    {
        var rig = NewRig();
        var platform = ControllerFor(rig, PlatformAdminUser());

        for (var i = 1; i <= 2; i++)
        {
            var dto = NewUser($"adm{i}", rig.AdminRoleA); dto.CollegeId = CollegeA;
            Assert.IsType<OkObjectResult>(await platform.CreateUser(dto));
        }

        var third = NewUser("adm3", rig.AdminRoleA); third.CollegeId = CollegeA;
        var bad = Assert.IsType<BadRequestObjectResult>(await platform.CreateUser(third));
        Assert.Contains("already has 2 admins", bad.Value!.ToString());
    }

    [Fact]
    public async Task Controller_platform_admin_must_name_the_college()
    {
        var rig = NewRig();
        var result = await ControllerFor(rig, PlatformAdminUser()).CreateUser(NewUser("adm", rig.AdminRoleA));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Controller_college_admin_ignores_a_college_id_in_the_body_and_cannot_mint_admins()
    {
        var rig = NewRig();
        var collegeAdmin = ControllerFor(rig, CollegeAdminUser());

        var dto = NewUser("t", rig.StaffRoleA);
        dto.CollegeId = CollegeB; // must be ignored
        Assert.IsType<OkObjectResult>(await collegeAdmin.CreateUser(dto));
        Assert.Equal(CollegeA, (await rig.Context.UserMasters.IgnoreQueryFilters().SingleAsync(u => u.Username == "t")).CollegeId);

        var admin = NewUser("boss", rig.AdminRoleA);
        Assert.IsType<BadRequestObjectResult>(await collegeAdmin.CreateUser(admin));
    }

    // ---------- platform admin creates a college; college admin cannot ----------

    [Fact]
    public async Task Platform_admin_can_create_a_college_and_a_college_admin_cannot()
    {
        Assert.Equal(AccessPolicies.PlatformAdmin, EffectivePolicy(typeof(CollegeDetailController), "Create"));
        Assert.True(await Passes(PlatformAdminUser(), AccessPolicies.PlatformAdmin));
        Assert.False(await Passes(CollegeAdminUser(), AccessPolicies.PlatformAdmin));

        // And the service really creates it (platform view, no CollegeId).
        var rig = NewRig();
        var service = new CollegeDetailService(rig.Context, new Mock<IFileStorage>().Object);
        var id = await service.CreateAsync(new CreateCollegeDTO
        {
            Name = "New College", CollegeCode = "NC", CollegeCenter = "NC1", ContactEmail = "n@c.d", ContactPhone = "9",
        });
        Assert.True(await rig.Context.Colleges.IgnoreQueryFilters().AnyAsync(c => c.CollegeId == id && c.Name == "New College"));
    }

    // ---------- RoleMaster: the admin role cannot be minted/renamed/deleted by a college admin ----------

    [Fact]
    public async Task College_admin_cannot_create_rename_into_or_delete_the_admin_role()
    {
        var rig = NewRig();
        var cu = new Mock<ICurrentUser>();
        cu.SetupGet(u => u.IsPlatformAdmin).Returns(false);
        cu.SetupGet(u => u.CollegeId).Returns(CollegeA);
        var roles = new RoleMasterService(rig.ScopedTo(CollegeA, platform: false), cu.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            roles.SaveRoleAsync(new CreateRoleDto { Name = "admin" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            roles.UpdateRoleAsync(new CreateRoleDto { RoleId = rig.StaffRoleA, Name = "Admin" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            roles.DeleteRoleAsync(rig.AdminRoleA));

        // Ordinary roles are unaffected.
        Assert.Equal("Role saved successfully", await roles.SaveRoleAsync(new CreateRoleDto { Name = "Clerk" }));

        // The platform admin may manage the admin role.
        var pu = new Mock<ICurrentUser>();
        pu.SetupGet(u => u.IsPlatformAdmin).Returns(true);
        Assert.Equal("Role saved successfully",
            await new RoleMasterService(rig.ScopedTo(null, platform: true), pu.Object).SaveRoleAsync(new CreateRoleDto { Name = "Admin" }));
    }
}
