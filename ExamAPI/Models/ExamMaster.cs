using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class ExamMaster : BaseEntity
    {
        [Key]
        public Guid ExamId { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        [MaxLength(50)]
        public string? ExamType { get; set; } // e.g., "Regular", "KT"

        [MaxLength(20)]
        public Guid? RevaluationForExamId { get; set; } // Academic Year Reference

        [MaxLength(20)]
        public string? Semester { get; set; }
        public Guid? AcademicYearAYID { get; set; }

        public bool? IsActive { get; set; }
        // Foreign Key
        public Guid? CourseId { get; set; }
        [ForeignKey(nameof(CourseId))]
        public CourseMaster? Course { get; set; }

        // Navigation Properties
        public ICollection<MarksMaster>? Marks { get; set; }
        public ICollection<TimeTableMaster>? TimeTables { get; set; }
    }
}