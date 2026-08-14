using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class ResolutionMaster : ICollegeScoped
    {

        /// <summary>Tenant owner. See <see cref="ICollegeScoped"/>.</summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }
        [Key]
        public Guid ID { get; set; }
        public Guid? ExamID { get; set; }
        public Guid? CreditID { get; set; }
        public Guid SubjectCreditID { get; set; }

        [StringLength(100)]
        public string? Head { get; set; }

        [StringLength(200)]
        public string? Resolution { get; set; }

        [StringLength(500)]
        public string? Remark { get; set; }

        public Guid? CourseID { get; set; }
        public Guid? AYID { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        // Navigation Properties

        public virtual ExamMaster? Exam { get; set; }
        public virtual SubjectCreditMaster? Credit { get; set; }
        public virtual SubjectCredits? SubjectCredit { get; set; }
        public virtual CourseMaster? Course { get; set; }
        [ForeignKey(nameof(AYID))]
        public virtual AcademicYear? AcademicYear { get; set; }
    }
}
