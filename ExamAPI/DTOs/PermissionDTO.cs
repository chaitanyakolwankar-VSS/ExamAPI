namespace ExamAPI.DTOs
{
    public class PermissionDTO
    {
    }

    public class PermissionModuleDto
    {
        public string PermissionModuleName { get; set; } = string.Empty;
        public List<PermissionFormDto> PermissionForms { get; set; } = new();
    }



    public class PermissionCreate
    {
        public string PermissionFormName { get; set; }= string.Empty;
        public string PermissionModuleName { get; set; }= string.Empty;
    }

    public class  PermissionUpdate
    {
        public string PermissionFormName { get; set; } = string.Empty;
        public string PermissionModuleName { get; set; } = string.Empty;
    }

    public class PermissionResponse
    {
        public Guid PermissionId { get; set; }
        public string PermissionFormName { get; set; } = string.Empty;
        public string PermissionModuleName { get; set; } = string.Empty;
    }
    public class PermissionFormDto
    {
        public Guid PermissionId { get; set; }
        public string PermissionFormName { get; set; } = string.Empty;
    }

}
