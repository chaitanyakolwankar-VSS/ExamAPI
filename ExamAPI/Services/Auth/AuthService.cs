using ExamAPI.Data;
using ExamAPI.DTOs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ExamAPI.Services.Auth
{
    public class AuthService : IAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;

        public AuthService(IConfiguration configuration, ApplicationDbContext context)
        {
            _configuration = configuration;
            _context = context;
        }


        public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto request)
        {
            // 1. Check for Dummy / Bypass Credentials
            if (request.Username.ToLower() == "admin" && request.Password == "admin")
            {
                var dummyCollegeId = Guid.Parse("103EBF99-FEB0-43BC-A312-56FE85D3BCC6");
                
                // Try to find the college in DB, or fallback to dummy
                var college = await _context.Colleges.FirstOrDefaultAsync(x => x.CollegeId == dummyCollegeId);

                var userDto = new UserDto
                {
                    UserId = Guid.NewGuid(),
                    Username = "admin",
                    Email = "admin@Test.com",
                    Role = "Admin",
                    CollegeID = dummyCollegeId.ToString()
                };

                var collegeDto = new CollegeDetailDTO
                {
                    CollegeId = college?.CollegeId ?? dummyCollegeId,
                    Name = college?.Name ?? "Dummy College"
                };

                var token = GenerateJwtToken(userDto);

                return new LoginResponseDto
                {
                    Token = token,
                    User = userDto,
                    College = collegeDto
                };
            }

            // 2. Fallback to Database Authentication
            var user = await _context.UserMasters
                .Include(x => x.Role)
                .FirstOrDefaultAsync(x => x.Username == request.Username && x.IsDeleted == false);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.HashedPassword))
            {
                throw new UnauthorizedAccessException("Invalid User Name or Password");
            }

            var collegeId = user.CollegeId ?? Guid.Parse("103EBF99-FEB0-43BC-A312-56FE85D3BCC6");
            var dbCollege = await _context.Colleges.FirstOrDefaultAsync(x => x.CollegeId == collegeId);

            var dbUserDto = new UserDto
            {
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role?.Name ?? "User",
                CollegeID = collegeId.ToString()
            };

            var dbCollegeDto = new CollegeDetailDTO
            {
                CollegeId = dbCollege?.CollegeId ?? collegeId,
                Name = dbCollege?.Name ?? "Dummy College"
            };

            var dbToken = GenerateJwtToken(dbUserDto);

            return new LoginResponseDto
            {
                Token = dbToken,
                User = dbUserDto,
                College = dbCollegeDto
            };
        } 

        private string GenerateJwtToken(UserDto user)
        {
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("CollegeId", user.CollegeID)
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddDays(7), // Token valid for 7 days 
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"],
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
