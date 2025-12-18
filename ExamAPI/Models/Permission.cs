using System.ComponentModel.DataAnnotations;

namespace ExamAPI.Models
{
    public class Permission : BaseEntity
    {
        [Key]
        public Guid PermissionId { get; set; }

        [Required]
        [MaxLength(100)]
        public required string PermissionFormName { get; set; }

        [Required]
        [MaxLength(100)]
        public required string PermissionModuleName { get; set; }

        // Navigation Properties
        public ICollection<RolePermission>? RolePermissions { get; set; }
        public ICollection<UserPermission>? UserPermissions { get; set; } 
    }
}