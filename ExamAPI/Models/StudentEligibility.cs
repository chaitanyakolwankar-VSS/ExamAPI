using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class StudentEligibility : BaseEntity
    {
        [Key]
        public Guid Id { get; set; }

        // Links to Student
        public Guid? StdMstId { get; set; }
        [ForeignKey(nameof(StdMstId))]
        public StudentMaster? Student { get; set; }

        // Links to Course
        public Guid? CourseId { get; set; }
        [ForeignKey(nameof(CourseId))]
        public CourseMaster? Course { get; set; }

        [MaxLength(20)]
        public string? AYID { get; set; } // Academic Year ID string

        [MaxLength(50)]
        public string? StudentId { get; set; } // String copy of ID for easy searching

        [MaxLength(20)]
        public string? SemesterId { get; set; } // e.g., "SEM-1"

        [MaxLength(50)]
        public string? Pattern { get; set; }
    }
}