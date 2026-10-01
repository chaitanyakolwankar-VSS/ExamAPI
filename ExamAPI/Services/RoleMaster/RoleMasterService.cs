using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Auth;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Data;


namespace ExamAPI.Services.RoleMaster
{
    public class RoleMasterService : IRoleMasterService
    {
        private readonly ApplicationDbContext _context;

        private readonly ICurrentUser _currentUser;

        public RoleMasterService(ApplicationDbContext context, ICurrentUser currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        // The admin role is identified by name (no schema flag), so a college admin must not be able to
        // mint, rename into, rename out of, or delete it: that would bypass the platform-admin-only,
        // 2-admins-per-college rule (DEC-17) enforced when the role is assigned to a user.
        private void EnsureAdminRoleNotTouched(string? newName, string? existingName)
        {
            if (_currentUser.IsPlatformAdmin) return;
            var touchesAdmin = AccessPolicies.IsAdminRoleName(newName) != AccessPolicies.IsAdminRoleName(existingName);
            if (touchesAdmin)
                throw new InvalidOperationException("Only the platform administrator can create, rename or delete the Admin role");
        }
        public async Task<List<RoleMasterDto>> GetRoleAsync()
        {
            var result =
            (from rm in _context.RoleMasters
             join rp in _context.RolePermissions on rm.RoleId equals rp.RoleId
             join p in _context.Permissions on rp.PermissionId equals p.PermissionId
             where !rm.IsDeleted && !rp.IsDeleted && !p.IsDeleted
             group p by new { rm.RoleId, rm.Name, rm.Description } into g
             select new RoleMasterDto
             {
                 RoleId = g.Key.RoleId,
                 Name = g.Key.Name,
                 Description = g.Key.Description,
                 PermissionFormNames = string.Join(", ", g.Select(x => x.PermissionFormName))
             }).ToList();
            return result;
        }
        public async Task<List<PermissionResponse>> GetPermissionsAsync()
        {
            var result = await _context.Permissions
                .OrderBy(x => x.PermissionModuleName)
       .ThenBy(x => x.PermissionFormName)
  .Select(x => new PermissionResponse
  {
      PermissionId = x.PermissionId,
      PermissionModuleName = x.PermissionModuleName,
      PermissionFormName = x.PermissionFormName
  })


       .ToListAsync();

            return result;
        }
        public async Task<RoleEditDto?> GetRoleByIdAsync(Guid roleId)
        {
            var role = await _context.RoleMasters
                .Include(r => r.RolePermissions)
                .Where(r => r.RoleId == roleId)
                .Select(r => new RoleEditDto
                {
                    RoleId = r.RoleId,
                    Name = r.Name,
                    Description = r.Description,
                    PermissionIds = r.RolePermissions!.Select(rp => rp.PermissionId).ToList()
                })
                .FirstOrDefaultAsync();

            return role;
        }
        public async Task<string> SaveRoleAsync(CreateRoleDto dto)
        {
            if (dto == null)
                return "Invalid data";

            EnsureAdminRoleNotTouched(dto.Name, existingName: null);

            var roleId = Guid.NewGuid();

            var role = new ExamAPI.Models.RoleMaster
            {
                RoleId = roleId,
                Name = dto.Name,
                Description = dto.Description,
                CreatedAt = DateTime.UtcNow
            };

            _context.RoleMasters.Add(role);

            if (dto.PermissionIds != null && dto.PermissionIds.Any())
            {
                var rolePermissions = dto.PermissionIds.Select(pid => new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = pid,
                    CreatedAt = DateTime.UtcNow
                }).ToList();

                _context.RolePermissions.AddRange(rolePermissions);
            }

            await _context.SaveChangesAsync();
            return "Role saved successfully";
        }

        public async Task<string> UpdateRoleAsync(CreateRoleDto dto)
        {
            if (dto == null || dto.RoleId == null || dto.RoleId == Guid.Empty)
                return "Invalid role";

            var role = await _context.RoleMasters
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.RoleId == dto.RoleId);

            if (role == null)
                return "Role not found";

            EnsureAdminRoleNotTouched(dto.Name, role.Name);

            role.Name = dto.Name;
            role.Description = dto.Description;
            role.UpdatedAt = DateTime.UtcNow;

            if (role.RolePermissions != null && role.RolePermissions.Any())
                _context.RolePermissions.RemoveRange(role.RolePermissions);

            if (dto.PermissionIds != null && dto.PermissionIds.Any())
            {
                var rolePermissions = dto.PermissionIds.Select(pid => new RolePermission
                {
                    RoleId = role.RoleId,
                    PermissionId = pid,
                    UpdatedAt = DateTime.UtcNow
                }).ToList();

                _context.RolePermissions.AddRange(rolePermissions);
            }

            await _context.SaveChangesAsync();
            return "Role updated successfully";
        }
        public async Task<string> DeleteRoleAsync(Guid roleId)
        {
            if (roleId == Guid.Empty)
                return "Invalid role ID";

            var role = await _context.RoleMasters
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.RoleId == roleId);

            if (role == null)
                return "Role not found";

            EnsureAdminRoleNotTouched(newName: null, existingName: role.Name);

            role.IsDeleted = true;

            if (role.RolePermissions != null && role.RolePermissions.Any())
            {
                foreach (var rp in role.RolePermissions)
                {
                    rp.IsDeleted = true;
                }
            }
            await _context.SaveChangesAsync();

            return "Role deleted successfully";
        }
    }
}
