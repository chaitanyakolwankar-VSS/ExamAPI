using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace ExamAPI.Services.Auth
{
    /// <summary>
    /// Access model (DEC-17 / DB-05). Defined once here; controllers reference the policy
    /// constants and nothing else knows the role name.
    /// <list type="bullet">
    /// <item><b>Platform admin</b> - <c>UserMaster.IsPlatformAdmin</c>, carried as the
    /// "IsPlatformAdmin" claim. Creates colleges and college admins.</item>
    /// <item><b>College admin</b> - a college user whose role (<c>RoleMaster.Name</c>, emitted as
    /// the role claim at login) is <see cref="AdminRoleName"/>, matched case-insensitively.
    /// At most <see cref="MaxAdminsPerCollege"/> per college. Manages users, roles, permissions
    /// and college details, and deletes/restores students.</item>
    /// </list>
    /// No schema change: "admin" is just a role name, so the cap is enforced in the service layer.
    /// </summary>
    public static class AccessPolicies
    {
        /// <summary>Only the platform (developer) login. Creates colleges.</summary>
        public const string PlatformAdmin = "PlatformAdmin";

        /// <summary>
        /// A college admin, OR a platform admin. Platform admins pass it deliberately: they onboard
        /// colleges and must create/replace college admins and edit any college's details. They
        /// carry no CollegeId, so on tenant-scoped data they see only platform-wide template rows.
        /// </summary>
        public const string CollegeAdmin = "CollegeAdmin";

        /// <summary>Name of the admin role (RoleMaster.Name). Case-insensitive match.</summary>
        public const string AdminRoleName = "Admin";

        public const int MaxAdminsPerCollege = 2;

        public static bool IsAdminRoleName(string? roleName) =>
            string.Equals(roleName?.Trim(), AdminRoleName, StringComparison.OrdinalIgnoreCase);

        public static bool IsPlatformAdmin(ClaimsPrincipal? user) =>
            string.Equals(user?.FindFirstValue("IsPlatformAdmin"), "true", StringComparison.OrdinalIgnoreCase);

        public static bool IsCollegeAdmin(ClaimsPrincipal? user) =>
            user?.Identity?.IsAuthenticated == true
            && user.FindAll(ClaimTypes.Role).Any(c => IsAdminRoleName(c.Value));

        public static void AddAccessPolicies(this AuthorizationOptions options)
        {
            options.AddPolicy(PlatformAdmin, p => p
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => IsPlatformAdmin(ctx.User)));

            options.AddPolicy(CollegeAdmin, p => p
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => IsPlatformAdmin(ctx.User) || IsCollegeAdmin(ctx.User)));
        }
    }
}
