using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class TimeTableMaster : BaseEntity, ICollegeScoped
    {

        /// <summary>Tenant owner. See <see cref="ICollegeScoped"/>.</summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }
        [Key]
        public Guid Id { get; set; }

        [MaxLength(50)]
        public string? Time { get; set; }

        [MaxLength(50)]
        public string? Date { get; set; }

        // Foreign Keys
        public Guid? ExamId { get; set; }
        [ForeignKey(nameof(ExamId))]
        public ExamMaster? Exam { get; set; }

        public Guid? CourseId { get; set; }
        [ForeignKey(nameof(CourseId))]
        public CourseMaster? Course { get; set; }

        public Guid? SubjectId { get; set; }
        [ForeignKey(nameof(SubjectId))]
        public SubjectMaster? Subject { get; set; }
    }
}