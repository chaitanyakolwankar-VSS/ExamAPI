using ExamAPI.DTOs;
using ExamAPI.Services.PasswordResetOTP;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ExamAPI.Services.Auth;
using System.Linq.Expressions;

namespace ExamAPI.Controllers
{
    // Exempt from the global fallback authorization policy: password reset is by
    // definition reached by users who cannot authenticate.
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.SignIn)]
    [Route("api/[controller]")]
    [ApiController]
    public class SendResetOtpController : ControllerBase
    {
        private readonly IPasswordResetService _passwordResetService;
        public SendResetOtpController(IPasswordResetService passwordResetService)
        {
            _passwordResetService = passwordResetService;
        }


        [HttpPost("send-reset-otp")]
        [EnableRateLimiting(RateLimits.SendOtp)]
        public async Task<IActionResult> SendResetOtp(
SendResetOtpDTO dto)
        {
            try
            {
                await _passwordResetService.SendResetOtpAsync(dto.UserID);
            }
            catch (InvalidOperationException ex)
            {
                // E.g. no platform default email account yet: say so instead of a bare server error.
                return BadRequest(new { message = ex.Message });
            }

            return Ok("OTP Generated");
        }


        [HttpPost("VerifyOtp")]
        public async Task<IActionResult> VerifyOTP([FromBody] ResetPasswordDTO dto)
        {
            try
            {
                await _passwordResetService.ResetPasswordAsync(dto);
                return Ok(new { message = "OTP verified Successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ExamAPI.Services.Common.SafeError.Message(ex) });
            }
        }

        public class EmailResetRequest
        {
            public string? Email { get; set; }
            public string? Otp { get; set; }
            public string? NewPassword { get; set; }
        }

        /// <summary>Sign-in page "Forgot password": the same answer whether or not the email has an account.</summary>
        [HttpPost("by-email/send")]
        [EnableRateLimiting(RateLimits.SendOtp)]
        public async Task<IActionResult> SendByEmail([FromBody] EmailResetRequest request)
        {
            try
            {
                await _passwordResetService.SendResetOtpByEmailAsync(request.Email);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            return Ok(new { message = "If an account uses this email, a code is on its way. Check your inbox (and spam)." });
        }

        [HttpPost("by-email/reset")]
        public async Task<IActionResult> ResetByEmail([FromBody] EmailResetRequest request)
        {
            try
            {
                await _passwordResetService.ResetPasswordByEmailAsync(request.Email, request.Otp, request.NewPassword);
                return Ok(new { message = "Password changed. Sign in with the new one." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("verify-otp-only")]
        public async Task<IActionResult> verifyOtp(VerifyOtpDTO dto)
        {
            try
            {
                await _passwordResetService.VerifyOtpOnlyAsync(dto.UserId, dto.Otp);
                return Ok(new { message = "Verified Successfully" });
            }
            catch(Exception ex)
            {
                return BadRequest(new { message = ExamAPI.Services.Common.SafeError.Message(ex) });
            }
        }


    }
}
