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
        public Guid?CourseId { get; set; }
        [ForeignKey(nameof(CourseId))]
        public CourseMaster? Course { get; set; }

      
        public Guid? AYID { get; set; } 

        [MaxLength(50)]
        public string? StudentId { get; set; }
        [MaxLength(20)]
        public string? SemesterId { get; set; } 

        [MaxLength(50)]
        public string? Pattern { get; set; }
    }
}