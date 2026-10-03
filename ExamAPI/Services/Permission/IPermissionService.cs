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

        /// <summary>
        /// The forms the CURRENT user may open: role permissions UNION user permissions, non-deleted,
        /// tenant-scoped. Empty when none are configured. Feeds the menu and route guard only; it is not
        /// server-side enforcement.
        /// </summary>
        Task<List<PermissionResponse>> GetMyAllowedFormsAsync();
    }
}
