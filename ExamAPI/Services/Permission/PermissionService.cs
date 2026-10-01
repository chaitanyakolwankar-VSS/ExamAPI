using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Permissions;
using ExamAPI.Services.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Permissions
{
    public class PermissionService : IPermissionService
    {
        private readonly ApplicationDbContext _context;
        private readonly ICurrentUser _currentUser;

        public PermissionService(ApplicationDbContext context, ICurrentUser currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<List<PermissionResponse>> GetMyAllowedFormsAsync()
        {
            // The user id comes from the JWT only (ICurrentUser), never from a request value.
            if (_currentUser.UserId is not Guid userId)
                return new List<PermissionResponse>();

            // UserMasters / RoleMasters carry the tenant + soft-delete query filters, so a user or a
            // role that does not belong to the caller's college (or is deleted) simply is not found.
            var roleId = await _context.UserMasters
                .Where(u => u.UserId == userId)
                .Select(u => u.RoleId)
                .FirstOrDefaultAsync();

            var fromUser = _context.UserPermissions
                .Where(up => up.UserId == userId)
                .Select(up => up.PermissionId);

            IQueryable<Guid> allowedIds = fromUser;
            if (roleId is Guid rid)
            {
                var fromRole = _context.RolePermissions
                    .Where(rp => rp.RoleId == rid
                                 && _context.RoleMasters.Any(r => r.RoleId == rp.RoleId))
                    .Select(rp => rp.PermissionId);
                allowedIds = fromRole.Union(fromUser);
            }

            // Permissions is soft-delete filtered, so a deleted form drops out of the menu.
            return await _context.Permissions
                .Where(p => allowedIds.Contains(p.PermissionId))
                .OrderBy(p => p.PermissionModuleName)
                .ThenBy(p => p.PermissionFormName)
                .Select(p => new PermissionResponse
                {
                    PermissionId = p.PermissionId,
                    PermissionModuleName = p.PermissionModuleName,
                    PermissionFormName = p.PermissionFormName
                })
                .ToListAsync();
        }

        public async Task<bool> CreatePermissionAsync(PermissionCreate dto)
        {
            var permission = new Permission
            {
                PermissionFormName = dto.PermissionFormName,
                PermissionModuleName = dto.PermissionModuleName,
                IsDeleted = false
            };

            _context.Permissions.Add(permission);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<List<string>> GetModulesAsync()
        {
            return await _context.Permissions
                .Where(x => !x.IsDeleted)
                .Select(x => x.PermissionModuleName)
                .Distinct()
                .ToListAsync();
        }

        public async Task<List<PermissionModuleDto>> GetGroupedPermissionsAsync()
        {
            return await _context.Permissions
                .Where(x => !x.IsDeleted)
                .GroupBy(x => x.PermissionModuleName)
                .Select(g => new PermissionModuleDto
                {
                    PermissionModuleName = g.Key,
                    PermissionForms = g.Select(p => new PermissionFormDto
                    {
                        PermissionId = p.PermissionId,
                        PermissionFormName = p.PermissionFormName
                    }).ToList()
                })
                .ToListAsync();
        }



        public async Task<bool> DeletePermissionAsync(Guid permissionId)
        {
            var permission = await _context.Permissions
                .FirstOrDefaultAsync(x => x.PermissionId == permissionId && !x.IsDeleted);

            if (permission == null)
                return false;

            permission.IsDeleted = true;
            return await _context.SaveChangesAsync() > 0;
        }


        public async Task<bool> UpdatePermissionAsync(Guid id, PermissionUpdate dto)
        {
            var permission = await _context.Permissions
                .FirstOrDefaultAsync(x => x.PermissionId == id && !x.IsDeleted);

            if (permission == null)
                return false;

            permission.PermissionFormName = dto.PermissionFormName;

            return await _context.SaveChangesAsync() > 0;
        }
    }
}
