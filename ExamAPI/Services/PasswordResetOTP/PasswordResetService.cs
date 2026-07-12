using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Services.Email;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace ExamAPI.Services.PasswordResetOTP
{
    public class PasswordResetService : IPasswordResetService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _email;
        public PasswordResetService(ApplicationDbContext context,IEmailService email)
        {
            _context = context;
            _email = email;
        }

        public async Task SendResetOtpAsync(Guid userId)
        {
            var user = await _context.UserMasters
                .FirstOrDefaultAsync(x =>
                    x.UserId == userId &&
                    !x.IsDeleted);

            if (user == null)
                throw new Exception("User not found");

            var otp = RandomNumberGenerator
                .GetInt32(100000, 999999)
                .ToString();

            var otpRecord = new Models.PasswordResetOTP
            {
                UserId = user.UserId,
                OTP = otp,
                CreatedAt = DateTime.UtcNow,
                ExpiryTime = DateTime.UtcNow.AddMinutes(10),
                IsUsed = false,
                AttemptCount = 0
            };

            _context.PasswordResetOTPs.Add(otpRecord);

            await _context.SaveChangesAsync();

            //await _email.SendEmailAsync(user.Email, "Password Reset OTP", $"Your OTP is {otp}. It is valid for 10 minutes.");
            await _email.SendEmailAsync(
    user.Email,
    "Password Reset OTP - Do Not Reply",
    $@"
    <div style='font-family:Arial,sans-serif; max-width:480px; 
                margin:auto; padding:24px; 
                border:1px solid #e5e7eb; border-radius:8px;'>

        <h2 style='color:#1f2937; margin-bottom:4px;'>Password Reset OTP</h2>
        <p style='color:#6b7280; margin-top:0;'>
            Use the OTP below to reset your password.
        </p>

        <div style='background:#f3f4f6; border-radius:8px; 
                    padding:20px; text-align:center; margin:24px 0;'>
            <p style='margin:0; color:#6b7280; font-size:13px;'>
                Your One-Time Password
            </p>
            <div style='font-size:36px; font-weight:bold; 
                        letter-spacing:10px; color:#2563eb; padding:12px 0;'>
                {otp}
            </div>
            <p style='margin:0; color:#6b7280; font-size:13px;'>
                Valid for <strong>10 minutes</strong> only
            </p>
        </div>

        <hr style='border:none; border-top:1px solid #e5e7eb; margin:16px 0;'/>

        <div style='background:#fef9c3; border:1px solid #fde68a; 
                    border-radius:6px; padding:12px; margin-bottom:16px;'>
            <p style='margin:0; color:#92400e; font-size:13px;'>
                ⚠️ <strong>Do not reply to this email.</strong> 
                This mailbox is not monitored and replies will not be received.
            </p>
        </div>

        <p style='color:#9ca3af; font-size:12px; margin:0;'>
            If you did not request a password reset, please ignore this email. 
            Your password will remain unchanged.
        </p>
    </div>
    "
);

        }

        public async Task ResetPasswordAsync(ResetPasswordDTO dto)
        {
            var otpRecord = await _context.PasswordResetOTPs
                .Where(x =>
                    x.UserId == dto.UserID &&
                    !x.IsUsed &&
                    x.ExpiryTime > DateTime.UtcNow)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (otpRecord == null)
                throw new Exception("No valid OTP found. Please request a new one.");

            otpRecord.AttemptCount++;

            if (otpRecord.AttemptCount > 5)
            {
                otpRecord.IsUsed = true; 
                await _context.SaveChangesAsync();
                throw new Exception("Too many failed attempts. Please request a new OTP.");
            }

            if (otpRecord.OTP != dto.Otp)
            {
                await _context.SaveChangesAsync();
                throw new Exception($"Invalid OTP. {5 - otpRecord.AttemptCount} attempts remaining.");
            }
            var user = await _context.UserMasters
                .FirstOrDefaultAsync(x =>
                    x.UserId == dto.UserID &&
                    !x.IsDeleted);

            if (user == null)
                throw new Exception("User not found.");

            user.HashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            otpRecord.IsUsed = true;

            await _context.SaveChangesAsync();
        }


        public async Task<bool> VerifyOtpOnlyAsync(Guid userId, string otp)
        {
            var otpRecord = await _context.PasswordResetOTPs
                .Where(x =>
                    x.UserId == userId &&
                    !x.IsUsed &&
                    x.ExpiryTime > DateTime.UtcNow)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (otpRecord == null)
                throw new Exception("No valid OTP found. Please request a new one.");

            otpRecord.AttemptCount++;

            if (otpRecord.AttemptCount > 5)
            {
                otpRecord.IsUsed = true;
                await _context.SaveChangesAsync();
                throw new Exception("Too many failed attempts. Please request a new OTP.");
            }

            if (otpRecord.OTP != otp)
            {
                await _context.SaveChangesAsync();
                throw new Exception($"Invalid OTP. {5 - otpRecord.AttemptCount} attempts remaining.");
            }

            await _context.SaveChangesAsync();
            return true;
        }
    }
}
