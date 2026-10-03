using System.Reflection;
using ExamAPI.Controllers;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Auth;
using ExamAPI.Services.Permissions;
using ExamAPI.Services.RoleMaster;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// T-05 / T-06: the menu follows the role's permissions (allowed forms = role permissions UNION user
/// permissions, non-deleted, tenant-scoped) and Role Master lists every role, including ones with no permissions.
/// </summary>
public sealed class PermissionMenuTests
{
    private static readonly Guid CollegeA = Guid.NewGuid();
    private static readonly Guid CollegeB = Guid.NewGuid();

    private sealed class Rig
    {
        public required DbContextOptions<ApplicationDbContext> Options { get; init; }
        public required ApplicationDbContext Seed { get; init; }

        public ApplicationDbContext Scoped(Guid? college)
        {
            var cu = new Mock<ICurrentUser>();
            cu.SetupGet(u => u.CollegeId).Returns(college);
            return new ApplicationDbContext(Options, new Mock<IHttpContextAccessor>().Object, cu.Object);
        }

        public PermissionService PermissionsFor(Guid? college, Guid? userId)
        {
            var cu = new Mock<ICurrentUser>();
            cu.SetupGet(u => u.CollegeId).Returns(college);
            cu.SetupGet(u => u.UserId).Returns(userId);
            return new PermissionService(new ApplicationDbContext(Options, new Mock<IHttpContextAccessor>().Object, cu.Object), cu.Object);
        }

        public RoleMasterService RolesFor(Guid? college)
        {
            var cu = new Mock<ICurrentUser>();
            cu.SetupGet(u => u.CollegeId).Returns(college);
            return new RoleMasterService(Scoped(college), cu.Object);
        }
    }

    private static Rig NewRig()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var platform = new Mock<ICurrentUser>();
        platform.SetupGet(u => u.IsPlatformAdmin).Returns(true);
        return new Rig
        {
            Options = options,
            Seed = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, platform.Object),
        };
    }

    private static Permission Perm(Rig rig, string module, string form, bool deleted = false)
    {
        var p = new Permission { PermissionId = Guid.NewGuid(), PermissionModuleName = module, PermissionFormName = form };
        rig.Seed.Permissions.Add(p);
        rig.Seed.SaveChanges();
        if (deleted) { p.IsDeleted = true; rig.Seed.SaveChanges(); }
        return p;
    }

    private static RoleMaster Role(Rig rig, string name, Guid? college, bool deleted = false)
    {
        var r = new RoleMaster { RoleId = Guid.NewGuid(), Name = name, CollegeId = college };
        rig.Seed.RoleMasters.Add(r);
        rig.Seed.SaveChanges();
        if (deleted) { r.IsDeleted = true; rig.Seed.SaveChanges(); }
        return r;
    }

    private static UserMaster User(Rig rig, string name, Guid? college, Guid? roleId)
    {
        var u = new UserMaster
        {
            UserId = Guid.NewGuid(), Username = name, HashedPassword = "x", Email = $"{name}@t.t",
            FirstName = name, LastName = "T", CollegeId = college, RoleId = roleId,
        };
        rig.Seed.UserMasters.Add(u);
        rig.Seed.SaveChanges();
        return u;
    }

    private static void GrantRole(Rig rig, RoleMaster role, Permission p, bool deleted = false)
    {
        rig.Seed.RolePermissions.Add(new RolePermission { RoleId = role.RoleId, PermissionId = p.PermissionId });
        rig.Seed.SaveChanges();
        if (deleted)
        {
            var rp = rig.Seed.RolePermissions.IgnoreQueryFilters().Single(x => x.RoleId == role.RoleId && x.PermissionId == p.PermissionId);
            rp.IsDeleted = true;
            rig.Seed.SaveChanges();
        }
    }

    private static void GrantUser(Rig rig, UserMaster user, Permission p)
    {
        rig.Seed.UserPermissions.Add(new UserPermission { UserId = user.UserId, PermissionId = p.PermissionId });
        rig.Seed.SaveChanges();
    }

    private static string[] Forms(IEnumerable<PermissionResponse> r) => r.Select(x => x.PermissionFormName).OrderBy(x => x).ToArray();

    // ---------- allowed forms ----------

    [Fact]
    public async Task Allowed_forms_are_the_roles_permissions()
    {
        var rig = NewRig();
        var examMaster = Perm(rig, "Academic Master", "Exam Master");
        var gazette = Perm(rig, "Reports", "Generate Gazette");
        Perm(rig, "Reports", "Statistical Report");   // exists, not granted
        var role = Role(rig, "Teacher", CollegeA);
        GrantRole(rig, role, examMaster);
        GrantRole(rig, role, gazette);
        var user = User(rig, "t1", CollegeA, role.RoleId);

        var forms = await rig.PermissionsFor(CollegeA, user.UserId).GetMyAllowedFormsAsync();

        Assert.Equal(new[] { "Exam Master", "Generate Gazette" }, Forms(forms));
        Assert.Contains(forms, f => f.PermissionModuleName == "Reports");
    }

    [Fact]
    public async Task Allowed_forms_are_role_union_user_permissions_without_duplicates()
    {
        var rig = NewRig();
        var shared = Perm(rig, "Academic Master", "Exam Master");
        var roleOnly = Perm(rig, "Reports", "Generate Gazette");
        var userOnly = Perm(rig, "Marks Entry", "Enter Marks");
        var role = Role(rig, "Teacher", CollegeA);
        GrantRole(rig, role, shared);
        GrantRole(rig, role, roleOnly);
        var user = User(rig, "t1", CollegeA, role.RoleId);
        GrantUser(rig, user, shared);     // also on the role: must appear once
        GrantUser(rig, user, userOnly);

        var forms = await rig.PermissionsFor(CollegeA, user.UserId).GetMyAllowedFormsAsync();

        Assert.Equal(new[] { "Enter Marks", "Exam Master", "Generate Gazette" }, Forms(forms));
    }

    [Fact]
    public async Task User_level_permissions_alone_are_enough_and_work_without_a_role()
    {
        var rig = NewRig();
        var p = Perm(rig, "Marks Entry", "Enter Marks");
        var user = User(rig, "norole", CollegeA, roleId: null);
        GrantUser(rig, user, p);

        var forms = await rig.PermissionsFor(CollegeA, user.UserId).GetMyAllowedFormsAsync();

        Assert.Equal(new[] { "Enter Marks" }, Forms(forms));
    }

    [Fact]
    public async Task A_role_with_no_permissions_yields_none_so_the_client_shows_only_the_dashboard()
    {
        var rig = NewRig();
        Perm(rig, "Reports", "Generate Gazette");
        var role = Role(rig, "Clerk", CollegeA);
        var user = User(rig, "c1", CollegeA, role.RoleId);

        Assert.Empty(await rig.PermissionsFor(CollegeA, user.UserId).GetMyAllowedFormsAsync());
    }

    [Fact]
    public async Task Deleted_permissions_deleted_grants_and_deleted_roles_are_ignored()
    {
        var rig = NewRig();
        var live = Perm(rig, "Reports", "Generate Gazette");
        var deletedForm = Perm(rig, "Reports", "Old Form", deleted: true);
        var revoked = Perm(rig, "Reports", "Revoked");
        var role = Role(rig, "Teacher", CollegeA);
        GrantRole(rig, role, live);
        GrantRole(rig, role, deletedForm);
        GrantRole(rig, role, revoked, deleted: true);
        var user = User(rig, "t1", CollegeA, role.RoleId);

        Assert.Equal(new[] { "Generate Gazette" }, Forms(await rig.PermissionsFor(CollegeA, user.UserId).GetMyAllowedFormsAsync()));

        role.IsDeleted = true;
        rig.Seed.SaveChanges();
        Assert.Empty(await rig.PermissionsFor(CollegeA, user.UserId).GetMyAllowedFormsAsync());
    }

    [Fact]
    public async Task Allowed_forms_never_come_from_another_colleges_role_or_user()
    {
        var rig = NewRig();
        var p = Perm(rig, "Reports", "Generate Gazette");
        var roleB = Role(rig, "Teacher", CollegeB);
        GrantRole(rig, roleB, p);
        var userB = User(rig, "b1", CollegeB, roleB.RoleId);
        var userA = User(rig, "a1", CollegeA, roleB.RoleId);   // tampered: college A user pointing at college B's role

        // the owner sees their grants...
        Assert.Equal(new[] { "Generate Gazette" }, Forms(await rig.PermissionsFor(CollegeB, userB.UserId).GetMyAllowedFormsAsync()));
        // ...a college A user cannot borrow college B's role...
        Assert.Empty(await rig.PermissionsFor(CollegeA, userA.UserId).GetMyAllowedFormsAsync());
        // ...and a token for college A cannot read a college B user's forms by id.
        Assert.Empty(await rig.PermissionsFor(CollegeA, userB.UserId).GetMyAllowedFormsAsync());
    }

    [Fact]
    public async Task Anonymous_caller_gets_nothing()
    {
        var rig = NewRig();
        Perm(rig, "Reports", "Generate Gazette");
        Assert.Empty(await rig.PermissionsFor(CollegeA, userId: null).GetMyAllowedFormsAsync());
    }

    [Fact]
    public void Permission_me_is_open_to_any_signed_in_user_and_the_admin_actions_stay_admin_only()
    {
        MethodInfo M(string n) => typeof(PermissionController).GetMethod(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;

        Assert.Null(typeof(PermissionController).GetCustomAttribute<AuthorizeAttribute>());   // no class-level admin policy any more
        var me = M("Me").GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(me);
        Assert.Null(me!.Policy);                                                              // authenticated only

        // College admins read the shared catalog to tick screens for their roles; only the platform admin edits it.
        foreach (var action in new[] { "GetModules", "GetGroupedPermissions" })
            Assert.Equal(AccessPolicies.CollegeAdmin, M(action).GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        foreach (var action in new[] { "Create", "Update", "Delete" })
            Assert.Equal(AccessPolicies.PlatformAdmin, M(action).GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    // ---------- Role Master lists every role (T-06) ----------

    [Fact]
    public async Task GetRole_lists_a_role_with_no_permissions_and_still_joins_permission_names()
    {
        var rig = NewRig();
        var admin = Role(rig, "Admin", CollegeA);                  // no permissions
        var teacher = Role(rig, "Teacher", CollegeA);
        GrantRole(rig, teacher, Perm(rig, "Reports", "Generate Gazette"));
        GrantRole(rig, teacher, Perm(rig, "Academic Master", "Exam Master"));
        GrantRole(rig, teacher, Perm(rig, "Academic Master", "Dead", deleted: true));
        Role(rig, "Gone", CollegeA, deleted: true);                // deleted roles stay hidden
        Role(rig, "OtherCollege", CollegeB);                       // other tenants stay hidden

        var list = await rig.RolesFor(CollegeA).GetRoleAsync();

        Assert.Equal(new[] { "Admin", "Teacher" }, list.Select(r => r.Name).ToArray());
        Assert.Equal("", list.Single(r => r.RoleId == admin.RoleId).PermissionFormNames);
        Assert.Equal("Exam Master, Generate Gazette", list.Single(r => r.RoleId == teacher.RoleId).PermissionFormNames);
    }

    [Fact]
    public async Task A_roles_permissions_can_be_assigned_and_replaced_from_role_master()
    {
        var rig = NewRig();
        var a = Perm(rig, "Reports", "Generate Gazette");
        var b = Perm(rig, "Academic Master", "Exam Master");
        var roles = rig.RolesFor(CollegeA);

        await roles.SaveRoleAsync(new CreateRoleDto { Name = "Clerk", PermissionIds = { a.PermissionId } });
        var id = (await roles.GetRoleAsync()).Single().RoleId;
        Assert.Equal(new[] { a.PermissionId }, (await roles.GetRoleByIdAsync(id))!.PermissionIds.ToArray());

        // Update keeps one permission, adds another (same-key remove + re-add in one save), then clears all.
        await rig.RolesFor(CollegeA).UpdateRoleAsync(new CreateRoleDto { RoleId = id, Name = "Clerk", PermissionIds = { a.PermissionId, b.PermissionId } });
        Assert.Equal(2, (await rig.RolesFor(CollegeA).GetRoleByIdAsync(id))!.PermissionIds.Count);

        await rig.RolesFor(CollegeA).UpdateRoleAsync(new CreateRoleDto { RoleId = id, Name = "Clerk" });
        Assert.Empty((await rig.RolesFor(CollegeA).GetRoleByIdAsync(id))!.PermissionIds);
        Assert.Equal("Clerk", (await rig.RolesFor(CollegeA).GetRoleAsync()).Single().Name);   // still listed with none
    }
}
