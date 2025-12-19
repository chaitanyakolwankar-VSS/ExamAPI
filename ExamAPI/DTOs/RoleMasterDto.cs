using ExamAPI.Models;

namespace ExamAPI.DTOs
{
    public class RoleMasterDto : BaseEntity
    {
        public Guid RoleId { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
    }
}
