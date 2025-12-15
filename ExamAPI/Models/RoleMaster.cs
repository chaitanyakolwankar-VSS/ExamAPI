using System.ComponentModel.DataAnnotations;

namespace ExamAPI.Models
{
    public class RoleMaster : BaseEntity
    {
        [Key]
        public Guid RoleId { get; set; }

        [Required]
        [MaxLength(50)]
        public required string Name { get; set; } 

        [MaxLength(255)]
        public string? Description { get; set; }

        public ICollection<UserMaster>? Users { get; set; }
        public ICollection<RolePermission>? RolePermissions { get; set; }
    }
}