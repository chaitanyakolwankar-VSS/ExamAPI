using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class SubjectMaster : BaseEntity
    {
        [Key]
        public Guid SubjectId { get; set; }

        [Required]
        [MaxLength(20)]
        public required string SubjectCode { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        [MaxLength(20)]
        public string? SemId { get; set; } // e.g. "1"

        [MaxLength(50)]
        public string? SemName { get; set; } // e.g. "Semester 1"

        [MaxLength(50)]
        public string? Pattern { get; set; }

        // Foreign Key
        public Guid? CourseId { get; set; }
        [ForeignKey(nameof(CourseId))]
        public CourseMaster? Course { get; set; }

        // Navigation Properties
        public ICollection<SubjectCreditMaster>? SubjectCreditMasters { get; set; }
    }
}