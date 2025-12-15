using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class UserPermission : BaseEntity
    {
        public Guid UserId { get; set; }
        [ForeignKey(nameof(UserId))]
        public UserMaster? User { get; set; }

        public Guid PermissionId { get; set; }
        [ForeignKey(nameof(PermissionId))]
        public Permission? Permission { get; set; }
    }
}