using Microsoft.AspNetCore.Http;

namespace ExamAPI.DTOs
{
    /// <summary>Staff Master filters. Type: "internal", "external" or empty for both.</summary>
    public class StaffFilter
    {
        public string? Search { get; set; }
        public Guid? DepartmentId { get; set; }
        public Guid? DesignationId { get; set; }
        public string? Type { get; set; }
        public bool IncludeInactive { get; set; }
    }

    public class StaffDto
    {
        public Guid StaffId { get; set; }
        public string? EmployeeCode { get; set; }
        public string Title { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string? MiddleName { get; set; }
        public string LastName { get; set; } = "";
        /// <summary>As the letters print it: "Prof. Asha R. Patil".</summary>
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public Guid? DepartmentId { get; set; }
        public string? Department { get; set; }
        public Guid? DesignationId { get; set; }
        public string? Designation { get; set; }
        public bool IsExternal { get; set; }
        public string? ExternalCollege { get; set; }
        public string? ExternalCity { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>Add or edit one member of staff (the Staff Master form).</summary>
    public class StaffSave
    {
        public string? EmployeeCode { get; set; }
        public string? Title { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public Guid? DepartmentId { get; set; }
        public Guid? DesignationId { get; set; }
        public bool IsExternal { get; set; }
        public string? ExternalCollege { get; set; }
        public string? ExternalCity { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class StaffOptionDto
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>How many staff use it; an entry in use cannot be deleted.</summary>
        public int StaffCount { get; set; }
    }

    public class StaffOptionSave
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
    }

    public class StaffImportRequest
    {
        public IFormFile? File { get; set; }
        /// <summary>Check the file and show what would change, without saving.</summary>
        public bool Preview { get; set; }
    }

    public static class StaffImportActions
    {
        public const string New = "New";
        public const string Update = "Update";
        public const string Unchanged = "Unchanged";
    }

    public class StaffImportRow
    {
        public int Row { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        /// <summary>StaffImportActions; empty when the row has errors.</summary>
        public string Action { get; set; } = "";
        public bool HasErrors { get; set; }
        public string? Note { get; set; }
    }

    public class StaffImportResult
    {
        public bool Success { get; set; }
        public bool Saved { get; set; }
        public string Message { get; set; } = "";
        public List<string> Errors { get; set; } = new();
        public List<StaffImportRow> Rows { get; set; } = new();
        /// <summary>Department / designation names the import adds to the lists.</summary>
        public List<string> NewOptions { get; set; } = new();
    }
}
