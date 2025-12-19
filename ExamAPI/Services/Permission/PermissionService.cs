using ExamAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Permission
{
    public class PermissionService:IPermissionService
    {
        private readonly ApplicationDbContext _context;

        public PermissionService(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task<List<string>> GetModulesAsync()
        {
            return await _context.Permissions
                .Where(x => !x.IsDeleted)
                .Select(x => x.PermissionModuleName)
                .Distinct()
                .ToListAsync();
        }

    }
}
