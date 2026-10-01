using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Auth;
using ExamAPI.Services.PasswordResetOTP;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace ExamAPI.Services.UsersMaster
{
    public class UserMasterService : IUserMasterService
    {
        private readonly ApplicationDbContext _context;
        public UserMasterService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <param name="collegeId">
        /// Taken from the caller's token by the controller -- never from the request body for an
        /// ordinary user. Accepting it from the client would let any authenticated user create a
        /// user inside another college. Only a platform admin (who has no college of their own)
        /// names the target college, and the controller passes it through here.
        /// </param>
        /// <param name="callerIsPlatformAdmin">
        /// Only a platform admin may create a user holding the admin role (DEC-17).
        /// </param>
        public async Task<UserMasterDTO> CreateUserAsync(CreateUserMasterDTO dto, Guid collegeId, bool callerIsPlatformAdmin = false)
        {
            // A platform admin sees no tenant-scoped rows through the query filter, so every
            // lookup below ignores the filter and scopes to collegeId explicitly.
            if (!await _context.Colleges.IgnoreQueryFilters()
                    .AnyAsync(c => c.CollegeId == collegeId && !c.IsDeleted))
                throw new InvalidOperationException("College not found");

            // Username is unique per college, so this check must be scoped to the college.
            if (await _context.UserMasters.IgnoreQueryFilters().AnyAsync(u =>
                    !u.IsDeleted && u.CollegeId == collegeId && u.Username.ToLower() == dto.Username.ToLower()))
                throw new InvalidOperationException("Username already exists");

            await EnsureRoleAssignableAsync(dto.RoleId, collegeId, callerIsPlatformAdmin, excludeUserId: null);

            // Email is the login identifier and stays globally unique, so this check is
            // deliberately NOT scoped -- and must ignore the tenant filter to catch a
            // clash with a user in another college.
            if (await _context.UserMasters
              .IgnoreQueryFilters()
              .AnyAsync(u => !u.IsDeleted && u.Email.ToLower() == dto.Email.ToLower()))
                throw new InvalidOperationException("Email already exists");

            var user = new UserMaster
            {
                UserId = Guid.NewGuid(),
                Username = dto.Username.Trim(),
                Email = dto.Email.Trim().ToLowerInvariant(),
                FirstName = dto.FirstName.Trim(),
                LastName = dto.LastName.Trim(),
                RoleId = dto.RoleId,
                CollegeId = collegeId,
                HashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                CreatedAt = DateTime.UtcNow

            };

            _context.UserMasters.Add(user);
            await _context.SaveChangesAsync();
           
            return new UserMasterDTO
            {
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName
            };
        }

        public async Task<List<UserListDTO>> GetAllUsersAsync()
        {
            return await _context.UserMasters
                .Where(u => !u.IsDeleted)
                .Select(u => new UserListDTO
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    FirstName = u.FirstName,
                    LastName = u.LastName
                })
                .ToListAsync();
        }

        public async Task<GetUserMasterDTO?> GetById(Guid id)
        {
            return await _context.UserMasters
                .Where(u => u.UserId == id)
                .Select(u => new GetUserMasterDTO
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Email = u.Email,
                    RoleId = u.RoleId
                })
                .FirstOrDefaultAsync();
        }

        public async Task<bool> DeleteUserById(Guid id, bool callerIsPlatformAdmin = false)
        {
            var user = await Users(callerIsPlatformAdmin)
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
                return false;

            // A college admin cannot remove an admin (that frees a slot of the 2-admin cap
            // and could lock the college out); the platform admin manages admins.
            if (!callerIsPlatformAdmin && await IsAdminRoleAsync(user.RoleId))
                throw new InvalidOperationException("Only the platform administrator can remove a college admin");

            user.IsDeleted = true;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateUserMaster(UpdateUserMasterDTO dto, bool callerIsPlatformAdmin = false)
        {
            var user = await Users(callerIsPlatformAdmin)
                .FirstOrDefaultAsync(x => x.UserId == dto.UserId && !x.IsDeleted);

            if (user == null)
                return false;

            // Changing a user into or out of the admin role is a platform-admin action and is
            // subject to the 2-admin cap; leaving the role as it is (e.g. an admin editing their
            // own name) is not.
            if (dto.RoleId != user.RoleId && user.CollegeId is Guid ownCollege)
            {
                await EnsureRoleAssignableAsync(dto.RoleId, ownCollege, callerIsPlatformAdmin, excludeUserId: user.UserId);
                if (!callerIsPlatformAdmin && await IsAdminRoleAsync(user.RoleId))
                    throw new InvalidOperationException("Only the platform administrator can change a college admin's role");
            }

            user.Username = dto.Username;
            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.Email = dto.Email;
            user.RoleId = dto.RoleId;

            _context.UserMasters.Update(user);
            await _context.SaveChangesAsync();

            return true;
        }

        // Platform admins carry no CollegeId, so the tenant filter would hide every user from them.
        private IQueryable<UserMaster> Users(bool callerIsPlatformAdmin) =>
            callerIsPlatformAdmin ? _context.UserMasters.IgnoreQueryFilters() : _context.UserMasters;

        private async Task<bool> IsAdminRoleAsync(Guid? roleId)
        {
            if (roleId is not Guid id) return false;
            var name = await _context.RoleMasters.IgnoreQueryFilters()
                .Where(r => r.RoleId == id)
                .Select(r => r.Name)
                .FirstOrDefaultAsync();
            return AccessPolicies.IsAdminRoleName(name);
        }

        /// <summary>
        /// Validates that <paramref name="roleId"/> may be given to a user of <paramref name="collegeId"/>:
        /// it must belong to that college (or be a platform template), and if it is the admin role
        /// the caller must be a platform admin and the college must still have a free admin slot
        /// (<see cref="AccessPolicies.MaxAdminsPerCollege"/>). There is no schema constraint for the
        /// cap -- "admin" is only a role name -- so it is enforced here.
        /// </summary>
        private async Task EnsureRoleAssignableAsync(Guid? roleId, Guid collegeId, bool callerIsPlatformAdmin, Guid? excludeUserId)
        {
            if (roleId is not Guid id) return;

            var role = await _context.RoleMasters.IgnoreQueryFilters()
                .Where(r => r.RoleId == id && !r.IsDeleted && (r.CollegeId == collegeId || r.CollegeId == null))
                .Select(r => new { r.Name })
                .FirstOrDefaultAsync();
            if (role == null)
                throw new InvalidOperationException("Role not found for this college");

            if (!AccessPolicies.IsAdminRoleName(role.Name))
                return;

            if (!callerIsPlatformAdmin)
                throw new InvalidOperationException("Only the platform administrator can assign the admin role");

            var adminCount = await _context.UserMasters.IgnoreQueryFilters()
                .Where(u => !u.IsDeleted && u.CollegeId == collegeId
                            && (excludeUserId == null || u.UserId != excludeUserId)
                            && u.Role != null && !u.Role.IsDeleted
                            && u.Role.Name.Trim().ToLower() == AccessPolicies.AdminRoleName.ToLower())
                .CountAsync();
            if (adminCount >= AccessPolicies.MaxAdminsPerCollege)
                throw new InvalidOperationException(
                    $"This college already has {AccessPolicies.MaxAdminsPerCollege} admins; remove one before adding another");
        }

        public async Task ChangePasswordAsync(ChangePasswordDTO dto)
        {
            var user=await _context.UserMasters
                .FirstOrDefaultAsync(x => x.UserId == dto.UserId && !x.IsDeleted);

            if (user == null)
                throw new Exception("User not found");

            bool isCurrentPasswordValid = BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.HashedPassword);

            if(!isCurrentPasswordValid)
                throw new Exception("Current password is incorrect");

            user.HashedPassword= BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

    }
}
