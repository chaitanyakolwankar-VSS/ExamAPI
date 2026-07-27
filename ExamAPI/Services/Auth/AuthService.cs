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
            // Authentication is database-only and keyed on EMAIL, not Username. Usernames
            // are unique only within a college, so they cannot identify a user across
            // tenants; email is globally unique, so this lookup yields exactly one user and
            // that user's CollegeId establishes the tenant for the whole session.
            // (The former admin/admin bypass was removed: it minted a genuine 7-day Admin
            // token for a hardcoded CollegeId under a UserId matching no real user.)
            var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

            // IgnoreQueryFilters: UserMaster is college-scoped, but at login there is no
            // established tenant yet -- filtering here would match nothing.
            var user = await _context.UserMasters
                .IgnoreQueryFilters()
                .Include(x => x.Role)
                .FirstOrDefaultAsync(x => x.Email.ToLower() == email && x.IsDeleted == false);

            // Returning null (rather than throwing) lets AuthController answer 401.
            // The throw previously escaped unhandled and surfaced as a 500.
            if (user == null || !VerifyPassword(request.Password, user.HashedPassword))
            {
                return null;
            }

            // Platform administrators (support/sales/dev) belong to no single college, so
            // they are issued a token with NO CollegeId claim. Every college-scoped
            // controller reads that claim and will reject them -- by design. They can only
            // use the platform endpoints.
            if (user.IsPlatformAdmin)
            {
                var platformUserDto = new UserDto
                {
                    UserId = user.UserId,
                    Username = user.Username,
                    Email = user.Email,
                    Role = user.Role?.Name ?? "PlatformAdmin",
                    CollegeID = string.Empty,
                    IsPlatformAdmin = true
                };

                return new LoginResponseDto
                {
                    Token = GenerateJwtToken(platformUserDto),
                    User = platformUserDto,
                    College = new CollegeDetailDTO()
                };
            }

            // An ordinary user with no college cannot be scoped to one, so refuse the login
            // rather than silently adopting them into a hardcoded college.
            if (user.CollegeId is not Guid collegeId)
            {
                return null;
            }

            // CollegeId is a FK, so a missing row means the tenant was deleted underneath
            // the user. Refuse rather than issue a token for a college that isn't there.
            // IgnoreQueryFilters: the tenant filter cannot apply yet -- we are in the act of
            // establishing which tenant this caller belongs to.
            var dbCollege = await _context.Colleges
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.CollegeId == collegeId && !x.IsDeleted);
            if (dbCollege == null)
            {
                return null;
            }

            var dbUserDto = new UserDto
            {
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role?.Name ?? "User",
                CollegeID = collegeId.ToString(),
                IsPlatformAdmin = false
            };

            var dbCollegeDto = new CollegeDetailDTO
            {
                CollegeId = dbCollege.CollegeId,
                Name = dbCollege.Name
            };

            var dbToken = GenerateJwtToken(dbUserDto);

            return new LoginResponseDto
            {
                Token = dbToken,
                User = dbUserDto,
                College = dbCollegeDto
            };
        }

        /// <summary>
        /// BCrypt.Verify throws SaltParseException on a value that is not a bcrypt hash.
        /// Several legacy UserMaster rows still hold plain/legacy values, which made login
        /// return HTTP 500 and leak that the account exists. Treat any unusable hash as a
        /// failed login instead.
        /// </summary>
        private static bool VerifyPassword(string password, string? storedHash)
        {
            if (string.IsNullOrWhiteSpace(storedHash))
            {
                return false;
            }

            try
            {
                return BCrypt.Net.BCrypt.Verify(password, storedHash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }

        private string GenerateJwtToken(UserDto user)
        {
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role)
            };

            // The CollegeId claim is the tenant boundary for the entire session. It is
            // emitted ONLY for real college users -- never as an empty string, which
            // Guid.TryParse would reject anyway and which would mask a bug as a 401.
            if (!string.IsNullOrWhiteSpace(user.CollegeID))
            {
                claims.Add(new Claim("CollegeId", user.CollegeID));
            }

            if (user.IsPlatformAdmin)
            {
                claims.Add(new Claim("IsPlatformAdmin", "true"));
            }

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
