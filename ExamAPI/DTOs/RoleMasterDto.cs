using ExamAPI.Models;

namespace ExamAPI.DTOs
{
    public class RoleMasterDto  
    {
        public Guid RoleId { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }

        public string? PermissionFormNames { get; set; }
        public string? PermissionForms { get; set; }
    }
    public class CreateRoleDto
    {
        public Guid? RoleId { get; set; }      
        public required string Name { get; set; }
        public string? Description { get; set; }
        public List<Guid> PermissionIds { get; set; } = new();
    }
    public class RoleEditDto
    {
        public Guid RoleId { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public List<Guid> PermissionIds { get; set; } = new();
        public List<string> PermissionFormNames { get; set; } = new List<string>();
    }

}
