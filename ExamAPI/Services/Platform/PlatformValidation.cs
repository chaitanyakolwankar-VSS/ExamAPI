using System.ComponentModel.DataAnnotations;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Platform
{
    /// <summary>Input checks and small helpers shared by the platform (developer) services.</summary>
    internal static class PlatformValidation
    {
        private static readonly EmailAddressAttribute EmailCheck = new();

        public static string Required(string? value, string label, int maxLength)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v))
                throw new ArgumentException($"{label} is required.");
            if (v.Length > maxLength)
                throw new ArgumentException($"{label} must be at most {maxLength} characters.");
            return v;
        }

        public static string? Optional(string? value, string label, int maxLength)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            if (v.Length > maxLength)
                throw new ArgumentException($"{label} must be at most {maxLength} characters.");
            return v;
        }

        /// <summary>
        /// Short label of an academic year. The client header shows and matches on ShortDuration, so a blank
        /// one falls back to the full duration instead of being stored as null.
        /// </summary>
        public static string? ShortYear(string? value, string fullDuration) =>
            Optional(value, "Academic year (short duration)", 30) ?? (fullDuration.Length <= 30 ? fullDuration : null);

        public static string Email(string? value, string label = "Email")
        {
            var v = Required(value, label, 255);
            if (!EmailCheck.IsValid(v))
                throw new ArgumentException($"{label} is not a valid email address.");
            return v;
        }

        public static (string Name, string Code) Branch(BranchInput? input)
        {
            if (input == null) throw new ArgumentException("Branch is required.");
            return (Required(input.Name, "Branch name", 100), Required(input.Code, "Branch code", 20));
        }

        /// <summary>Validates and trims an admin; the password rule (min 8) matches CreateUserMasterDTO.</summary>
        public static CreateUserMasterDTO Admin(AdminInput? input, Guid roleId, Guid collegeId)
        {
            if (input == null) throw new ArgumentException("Admin details are required.");
            var password = input.Password ?? string.Empty;
            if (password.Length < 8)
                throw new ArgumentException("Admin password must be at least 8 characters.");
            return new CreateUserMasterDTO
            {
                Username = Required(input.Username, "Admin username", 50),
                Email = Email(input.Email, "Admin email"),
                FirstName = Required(input.FirstName, "Admin first name", 50),
                LastName = Required(input.LastName, "Admin last name", 50),
                Password = password,
                RoleId = roleId,
                CollegeId = collegeId,
            };
        }

        /// <summary>
        /// The college's "Admin" role, created when missing. Roles are per college and the platform
        /// admin sees no tenant rows, so the filter is ignored and the college named explicitly.
        /// </summary>
        public static async Task<(Models.RoleMaster Role, bool Created)> EnsureAdminRoleAsync(
            Data.ApplicationDbContext context, Guid collegeId, CancellationToken ct)
        {
            var existing = await context.RoleMasters.IgnoreQueryFilters()
                .Where(r => !r.IsDeleted && r.CollegeId == collegeId
                            && r.Name.Trim().ToLower() == AccessPolicies.AdminRoleName.ToLower())
                .FirstOrDefaultAsync(ct);
            if (existing != null) return (existing, false);

            var role = new Models.RoleMaster
            {
                RoleId = Guid.NewGuid(),
                Name = AccessPolicies.AdminRoleName,
                CollegeId = collegeId,
            };
            context.RoleMasters.Add(role);
            await context.SaveChangesAsync(ct);
            return (role, true);
        }
    }
}
