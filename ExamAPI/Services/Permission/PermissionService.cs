using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Permissions;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Permissions
{
    public class PermissionService : IPermissionService
    {
        private readonly ApplicationDbContext _context;

        public PermissionService(ApplicationDbContext context)
        {
            _context = context;
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

        //public async Task<List<PermissionModuleDto>> GetGroupedPermissionsAsync()
        //{
        //    return await _context.Permissions
        //        .Where(x => !x.IsDeleted)
        //        .GroupBy(x => x.PermissionModuleName)
        //        .Select(g => new PermissionModuleDto
        //        {
        //            PermissionModuleName = g.Key,
        //            PermissionForms = g
        //                .Select(p => p.PermissionFormName)
        //                .ToList()
        //        })
        //        .ToListAsync();
        //}
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
