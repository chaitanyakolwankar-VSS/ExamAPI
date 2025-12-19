using ExamAPI.DTOs;

namespace ExamAPI.Services.Common.RoleMaster
{
    public interface IRoleMasterService
    {
        Task<List<RoleMasterDto>> GetRoleAsync();
    }
}








