using ExamAPI.Data;
using ExamAPI.DTOs;
using Microsoft.EntityFrameworkCore;


namespace ExamAPI.Services.RoleMaster
{
    public class RoleMasterService : IRoleMasterService
    {
        private readonly ApplicationDbContext _context;

        public RoleMasterService(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task<List<RoleMasterDto>> GetRoleAsync()
        { 

            var result =
    (from r in _context.RoleMasters
     join rp in _context.RolePermissions on r.RoleId equals rp.RoleId
     join p1 in _context.Permissions on rp.PermissionId equals p1.PermissionId
     join p2 in _context.Permissions on p1.PermissionModuleName equals p2.PermissionModuleName
     where !r.IsDeleted && !p1.IsDeleted && !p2.IsDeleted
     group p2 by new
     {
         r.Name,
         r.Description,
         p2.PermissionModuleName
     } into g
     select new RoleMasterDto
     {
         Name = g.Key.Name,
         Description = g.Key.Description,
         PermissionFormNames = g.Key.PermissionModuleName,
         PermissionForms = string.Join(", ", g.Select(x => x.PermissionFormName))
     }).ToList();
            return result;

        }

    }
}
 



