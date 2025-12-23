using ExamAPI.DTOs;

namespace ExamAPI.Services.RoleMaster
{
    public interface IRoleMasterService
    {
        Task<List<RoleMasterDto>> GetRoleAsync();
    }
}








