namespace ExamAPI.DTOs
{
    public class AuthDtos
    {
    }

    public class LoginRequestDto
    {
        /// <summary>
        /// Email is the login identifier, not Username. Usernames are only unique within a
        /// college (two colleges may each have an "admin"), so they cannot identify a user
        /// on their own. Email is globally unique, so one lookup resolves to exactly one
        /// user -- and that user's CollegeId is what establishes the tenant for the session.
        /// </summary>
        public required string Email { get; set; }
        public required string Password { get; set; }
    }

    public class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; }
        public CollegeDetailDTO College { get; set; } = new();
    }

    public class UserDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;

        /// <summary>Empty for platform administrators, who belong to no single college.</summary>
        public string CollegeID { get; set; } = string.Empty;

        public bool IsPlatformAdmin { get; set; }
    }
}
