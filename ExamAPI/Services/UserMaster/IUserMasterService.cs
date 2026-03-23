using ExamAPI.DTOs;

namespace ExamAPI.Services.UsersMaster
{
    public interface IUserMasterService
    {
        Task<UserMasterDTO> CreateUserAsync(CreateUserMasterDTO dto);

        Task<List<UserListDTO>> GetAllUsersAsync();

        Task<GetUserMasterDTO> GetById(Guid id);

        Task<bool> DeleteUserById(Guid id);
        Task<bool> UpdateUserMaster(UpdateUserMasterDTO dto);
    }
}
