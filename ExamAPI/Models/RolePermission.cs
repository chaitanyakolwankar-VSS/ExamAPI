using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class RolePermission : BaseEntity
    {
        public Guid RoleId { get; set; }
        [ForeignKey(nameof(RoleId))]
        public RoleMaster? Role { get; set; }

        public Guid PermissionId { get; set; }
        [ForeignKey(nameof(PermissionId))]
        public Permission? Permission { get; set; }
    }
}
