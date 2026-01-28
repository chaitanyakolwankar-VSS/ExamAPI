using ExamAPI.DTOs;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ExamAPI.Services.Auth
{
    public class AuthService : IAuthService
    {
        private readonly IConfiguration _configuration;

        public AuthService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto request)
        {
            // 1. HARDCODED User 
            if (request.Username.ToLower() == "admin" && request.Password == "admin")
            {
                var user = new UserDto
                {
                    UserId = Guid.NewGuid(),
                    Username = "admin",
                    Email = "admin@Test.com",
                    Role = "Admin",
                    CollegeId = "103EBF99-FEB0-43BC-A312-56FE85D3BCC6"
                };

                var token = GenerateJwtToken(user);

                return new LoginResponseDto
                {
                    Token = token,
                    User = user
                };
            }

            // TODO: Later, connect this to database:
           

            return null; // if login fails
        }

        private string GenerateJwtToken(UserDto user)
        {
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("CollegeId", user.CollegeId)
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
