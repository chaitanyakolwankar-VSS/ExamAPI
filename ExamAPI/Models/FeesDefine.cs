using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    [Table("FeesDefines")]
    public class FeesDefines : BaseEntity
    {

        [Key]
        public Guid FeesDefineId { get; set; }
        public Guid? Ayid { get; set; }

        [ForeignKey(nameof(Ayid))]
        public AcademicYear? AcademicYear { get; set; }
        public Guid? ExamId { get; set; }
        [ForeignKey(nameof(ExamId))]
        public ExamMaster? Exam { get; set; }


        [MaxLength(50)]
        public string? ExamType { get; set; }

        [MaxLength(50)]
        public string? SemId { get; set; }

        // Foreign Key
        public Guid? CourseId { get; set; }
        [ForeignKey(nameof(CourseId))]
        public CourseMaster? Course { get; set; }

        [MaxLength(100)]
        public string? Category { get; set; }

        public string SubCount { get; set; } = string.Empty;

        [Column(TypeName = "number")]
        public decimal Amount { get; set; }

    }
}