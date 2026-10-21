using ExamAPI.DTOs;

namespace ExamAPI.Services.PasswordResetOTP
{
    public interface IPasswordResetService
    {
        Task SendResetOtpAsync(Guid userId);
        Task ResetPasswordAsync(ResetPasswordDTO dto);
        Task<bool> VerifyOtpOnlyAsync(Guid UserId, string otp);

        /// <summary>Sign-in page "Forgot password": sends a code when the email belongs to a user; says nothing either way.</summary>
        Task SendResetOtpByEmailAsync(string? email);

        /// <summary>Sign-in page "Forgot password": the code from the email and the new password.</summary>
        Task ResetPasswordByEmailAsync(string? email, string? otp, string? newPassword);
    }
}
