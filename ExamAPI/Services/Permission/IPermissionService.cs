using ExamAPI.DTOs;

namespace ExamAPI.Services.Permissions
{
    public interface IPermissionService
    {
        Task<List<string>> GetModulesAsync();
        Task<bool> CreatePermissionAsync(PermissionCreate dto);
        Task<List<PermissionModuleDto>> GetGroupedPermissionsAsync();
        Task<bool> UpdatePermissionAsync(Guid id, PermissionUpdate dto);
        Task<bool> DeletePermissionAsync(Guid permissionId);
    }
}
