using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class UserMaster : BaseEntity, ICollegeScoped
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

        /// <summary>
        /// Null only for platform administrators (see <see cref="IsPlatformAdmin"/>).
        /// Every ordinary user is hard-bound to exactly one college.
        /// </summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? College { get; set; }

        /// <summary>
        /// Platform (support/sales/dev) staff who operate ABOVE any single tenant: they
        /// onboard colleges and are the only principals allowed to bypass the global
        /// college query filter. They carry no CollegeId, so they cannot use the ordinary
        /// college-scoped endpoints at all.
        /// </summary>
        public bool IsPlatformAdmin { get; set; }

        // Navigation Properties
        public ICollection<UserPermission>? UserPermissions { get; set; }
    }
}