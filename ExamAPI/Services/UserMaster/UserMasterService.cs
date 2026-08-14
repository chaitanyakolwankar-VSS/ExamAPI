using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.PasswordResetOTP;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace ExamAPI.Services.UsersMaster
{
    public class UserMasterService : IUserMasterService
    {
        private readonly ApplicationDbContext _context;
        public UserMasterService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <param name="collegeId">
        /// Taken from the caller's token by the controller -- never from the request body.
        /// Accepting it from the client would let any authenticated user create a user
        /// inside another college.
        /// </param>
        public async Task<UserMasterDTO> CreateUserAsync(CreateUserMasterDTO dto, Guid collegeId)
        {
            // Username is unique per college, so this check must be scoped to the college.
            if (await _context.UserMasters.AnyAsync(u =>
                    u.CollegeId == collegeId && u.Username.ToLower() == dto.Username.ToLower()))
                throw new InvalidOperationException("Username already exists");

            // Email is the login identifier and stays globally unique, so this check is
            // deliberately NOT scoped -- and must ignore the tenant filter to catch a
            // clash with a user in another college.
            if (await _context.UserMasters
              .IgnoreQueryFilters()
              .AnyAsync(u => !u.IsDeleted && u.Email.ToLower() == dto.Email.ToLower()))
                throw new InvalidOperationException("Email already exists");

            var user = new UserMaster
            {
                UserId = Guid.NewGuid(),
                Username = dto.Username.Trim(),
                Email = dto.Email.Trim().ToLowerInvariant(),
                FirstName = dto.FirstName.Trim(),
                LastName = dto.LastName.Trim(),
                RoleId = dto.RoleId,
                CollegeId = collegeId,
                HashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                CreatedAt = DateTime.UtcNow

            };

            _context.UserMasters.Add(user);
            await _context.SaveChangesAsync();
           
            return new UserMasterDTO
            {
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName
            };
        }

        public async Task<List<UserListDTO>> GetAllUsersAsync()
        {
            return await _context.UserMasters
                .Where(u => !u.IsDeleted)
                .Select(u => new UserListDTO
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    FirstName = u.FirstName,
                    LastName = u.LastName
                })
                .ToListAsync();
        }

        public async Task<GetUserMasterDTO?> GetById(Guid id)
        {
            return await _context.UserMasters
                .Where(u => u.UserId == id)
                .Select(u => new GetUserMasterDTO
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Email = u.Email,
                    RoleId = u.RoleId
                })
                .FirstOrDefaultAsync();
        }

        public async Task<bool> DeleteUserById(Guid id)
        {
            var user = await _context.UserMasters
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
                return false;

            user.IsDeleted = true;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateUserMaster(UpdateUserMasterDTO dto)
        {
            var user = await _context.UserMasters
                .FirstOrDefaultAsync(x => x.UserId == dto.UserId && !x.IsDeleted);

            if (user == null)
                return false;

            user.Username = dto.Username;
            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.Email = dto.Email;
            user.RoleId = dto.RoleId;

            _context.UserMasters.Update(user);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task ChangePasswordAsync(ChangePasswordDTO dto)
        {
            var user=await _context.UserMasters
                .FirstOrDefaultAsync(x => x.UserId == dto.UserId && !x.IsDeleted);

            if (user == null)
                throw new Exception("User not found");

            bool isCurrentPasswordValid = BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.HashedPassword);

            if(!isCurrentPasswordValid)
                throw new Exception("Current password is incorrect");

            user.HashedPassword= BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

    }
}
