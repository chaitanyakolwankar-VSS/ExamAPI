using System.ComponentModel.DataAnnotations;

namespace ExamAPI.DTOs
{

    public class CreateUserMasterDTO
    {
        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        public Guid? RoleId { get; set; }

        // For an ordinary caller CollegeId is deliberately NOT accepted from the client: it is
        // taken from the caller's token, otherwise any authenticated user could create a user
        // inside another college. This field is honoured ONLY when the caller is a platform
        // admin (who has no college of their own) creating a college admin; the controller
        // ignores it for everyone else.
        public Guid? CollegeId { get; set; }
    }
    public class UserMasterDTO
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    public class UserListDTO
    {
        public Guid UserId { get; set; }
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }

    public class GetUserMasterDTO
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public Guid? RoleId { get; set; }
        public Guid? CollegeId { get; set; }
    }

    public class DeleteUser
    {
        public Guid UserId { get; set; }
    }

    public class UpdateUserMasterDTO
    {
        public Guid UserId { get; set; }
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public Guid? RoleId { get; set; }
    }
}
