using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class AcademicYear : BaseEntity
    {
        [Key]
        public Guid AYID { get; set; }

        [Required]
        [MaxLength(50)] 
        public required string FullDuration { get; set; }

        [MaxLength(30)] 
        public string? ShortDuration { get; set; }

        public bool IsCurrent { get; set; }


        // Foreign Key
        public Guid CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? College { get; set; }

        // Navigation Properties 
        public ICollection<SubjectCreditMaster>? SubjectCredits { get; set; }
        public ICollection<ExamMaster>? Exams { get; set; }
        public ICollection<MarksMaster>? Marks { get; set; }
    }
}