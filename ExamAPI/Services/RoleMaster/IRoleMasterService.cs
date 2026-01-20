using ExamAPI.DTOs;
using ExamAPI.Models;

namespace ExamAPI.Services.RoleMaster
{
    public interface IRoleMasterService
    {
        Task<List<RoleMasterDto>> GetRoleAsync();
        Task<List<PermissionResponse>> GetPermissionsAsync(); 
        Task<RoleEditDto?> GetRoleByIdAsync(Guid roleId);
        Task<string> SaveRoleAsync(CreateRoleDto dto);
        Task<string> UpdateRoleAsync(CreateRoleDto dto);
        Task<string> DeleteRoleAsync(Guid roleId);

    }
}








