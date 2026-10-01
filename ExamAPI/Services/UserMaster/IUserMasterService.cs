using ExamAPI.DTOs;

namespace ExamAPI.Services.UsersMaster
{
    public interface IUserMasterService
    {
        Task<UserMasterDTO> CreateUserAsync(CreateUserMasterDTO dto, Guid collegeId, bool callerIsPlatformAdmin = false);

        Task<List<UserListDTO>> GetAllUsersAsync();

        Task<GetUserMasterDTO> GetById(Guid id);

        Task<bool> DeleteUserById(Guid id, bool callerIsPlatformAdmin = false);
        Task<bool> UpdateUserMaster(UpdateUserMasterDTO dto, bool callerIsPlatformAdmin = false);
        Task ChangePasswordAsync(ChangePasswordDTO dto);
    }
}
