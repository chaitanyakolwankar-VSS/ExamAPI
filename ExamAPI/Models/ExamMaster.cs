using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class ExamMaster : BaseEntity, ICollegeScoped
    {

        /// <summary>Tenant owner. See <see cref="ICollegeScoped"/>.</summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }
        [Key]
        public Guid ExamId { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        [MaxLength(50)]
        public string? ExamType { get; set; } // e.g., "Regular", "KT"

        
        public Guid? RevaluationForExamId { get; set; } // Academic Year Reference

        
        public string? Semester { get; set; }
        public Guid? AcademicYearAYID { get; set; }

        public bool? IsActive { get; set; }
        public bool IsLocked { get; set; }
        // Foreign Key
        public Guid? CourseId { get; set; }
        [ForeignKey(nameof(CourseId))]
        public CourseMaster? Course { get; set; }

        // Navigation Properties
        public ICollection<MarksMaster>? Marks { get; set; }
        public ICollection<TimeTableMaster>? TimeTables { get; set; }
    }
}