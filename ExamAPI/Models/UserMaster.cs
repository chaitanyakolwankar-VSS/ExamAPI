using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class UserMaster : BaseEntity
    {
        [Key]
        public Guid UserId { get; set; }

        [Required]
        [MaxLength(50)]
        public required string Username { get; set; }

        [Required]
        [MaxLength(255)] // Hash strings are long
        public required string HashedPassword { get; set; }

        [Required]
        [MaxLength(255)]
        [EmailAddress]
        public required string Email { get; set; }

        [Required]
        [MaxLength(50)]
        public required string FirstName { get; set; }

        [Required]
        [MaxLength(50)]
        public required string LastName { get; set; }

        // Foreign Keys
        public Guid? RoleId { get; set; }
        [ForeignKey(nameof(RoleId))]
        public RoleMaster? Role { get; set; }

        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? College { get; set; }

        // Navigation Properties
        public ICollection<UserPermission>? UserPermissions { get; set; }
    }
}