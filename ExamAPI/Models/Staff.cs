using System.ComponentModel.DataAnnotations;

namespace ExamAPI.Models
{
    /// <summary>
    /// A member of staff who receives exam letters (supervision duty, examiner / moderator appointment). Not a
    /// sign-in: most supervisors and examiners never sign in, so this is separate from <see cref="UserMaster"/>.
    /// Holds only what the letters print or what staff are picked by. External examiners (professors of other
    /// colleges) are staff too, with their own college and city.
    /// </summary>
    public class StaffMaster : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid StaffId { get; set; }
        public Guid? CollegeId { get; set; }

        /// <summary>The college's employee ID. Required for our own staff; external staff are matched by email.</summary>
        [MaxLength(30)]
        public string? EmployeeCode { get; set; }

        [MaxLength(10)]
        public required string Title { get; set; }   // StaffTitles
        [MaxLength(50)]
        public required string FirstName { get; set; }
        [MaxLength(50)]
        public string? MiddleName { get; set; }
        [MaxLength(50)]
        public string LastName { get; set; } = "";

        /// <summary>Where the letters and the one-time codes are sent. Unique in the college.</summary>
        [MaxLength(256)]
        public required string Email { get; set; }

        public Guid? DepartmentId { get; set; }
        public StaffOption? Department { get; set; }
        public Guid? DesignationId { get; set; }
        public StaffOption? Designation { get; set; }

        public bool IsExternal { get; set; }
        /// <summary>External staff only: their own college and city (printed on the appointment letter).</summary>
        [MaxLength(200)]
        public string? ExternalCollege { get; set; }
        [MaxLength(100)]
        public string? ExternalCity { get; set; }

        /// <summary>Left the college: kept for old letters, not offered for new ones.</summary>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>A college's own list of departments or designations, the drop-downs on Staff Master and the Excel template.</summary>
    public class StaffOption : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }
        public Guid? CollegeId { get; set; }

        [MaxLength(20)]
        public required string Kind { get; set; }   // StaffOptionKinds
        [MaxLength(100)]
        public required string Name { get; set; }
    }

    public static class StaffOptionKinds
    {
        public const string Department = "Department";
        public const string Designation = "Designation";

        public static readonly IReadOnlyList<string> All = new[] { Department, Designation };

        /// <summary>The kind's spelling ("department" -> "Department"), or null when it is not one.</summary>
        public static string? Canonical(string? kind) =>
            All.FirstOrDefault(k => string.Equals(k, (kind ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static class StaffTitles
    {
        public static readonly IReadOnlyList<string> All = new[] { "Prof.", "Dr.", "Mr.", "Mrs.", "Ms.", "Miss" };

        /// <summary>The list's spelling of a title typed any way ("dr", "DR." -> "Dr."), or null when it is not one.</summary>
        public static string? Canonical(string? title)
        {
            var bare = (title ?? "").Trim().TrimEnd('.');
            return All.FirstOrDefault(t => string.Equals(t.TrimEnd('.'), bare, StringComparison.OrdinalIgnoreCase));
        }
    }
}
