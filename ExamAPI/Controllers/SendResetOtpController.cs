using ExamAPI.DTOs;
using ExamAPI.Services.PasswordResetOTP;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Linq.Expressions;

namespace ExamAPI.Controllers
{
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
        public async Task<IActionResult> SendResetOtp(
SendResetOtpDTO dto)
        {
            await _passwordResetService
                .SendResetOtpAsync(dto.UserID);

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
                return BadRequest(new { message = ex.Message });
            }
        }


    }
}
