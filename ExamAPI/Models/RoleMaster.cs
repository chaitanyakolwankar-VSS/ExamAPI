using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class RoleMaster : BaseEntity, ICollegeTemplatable
    {

        /// <summary>Tenant owner. See <see cref="ICollegeScoped"/>.</summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }
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