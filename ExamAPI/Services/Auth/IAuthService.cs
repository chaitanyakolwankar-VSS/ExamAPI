using ExamAPI.DTOs;

namespace ExamAPI.Services.Auth
{
    public interface IAuthService
    {
        Task<LoginResponseDto?> LoginAsync(LoginRequestDto request);
    }
}
