using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class CourseMaster : BaseEntity
    {
        [Key]
        public Guid CourseId { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        [Required]
        [MaxLength(20)]
        public required string CourseCode { get; set; }

        // Foreign Key
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? College { get; set; }

        // Navigation Properties
        public ICollection<SubjectMaster>? Subjects { get; set; }

        public ICollection<ExamMaster>? Exams { get; set; }
<<<<<<< Updated upstream
        public Guid AcademicYearAYID { get; internal set; }
=======

>>>>>>> Stashed changes
    }
}