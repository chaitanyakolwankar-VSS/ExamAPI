using ExamAPI.Data;
using ExamAPI.DTOs;
using Microsoft.EntityFrameworkCore;


namespace ExamAPI.Services.Common.RoleMaster
{
    public class RoleMasterService:IRoleMasterService
    {
        private readonly ApplicationDbContext _context;

        public RoleMasterService(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task<List<RoleMasterDto>> GetRoleAsync()
        {
            var role = await _context.RoleMasters
                .Select(r => new RoleMasterDto
                {
                    RoleId = r.RoleId,
                    Name = r.Name,
                    Description = r.Description
                })
                .ToListAsync();
            return role;
        }
    }
}


 


 
