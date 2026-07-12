using ExamAPI.DTOs;

namespace ExamAPI.Services.PasswordResetOTP
{
    public interface IPasswordResetService
    {
        Task SendResetOtpAsync(Guid userId);
        Task ResetPasswordAsync(ResetPasswordDTO dto);
        Task<bool> VerifyOtpOnlyAsync(Guid UserId, string otp);
    }
}
