using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class MarksMaster : BaseEntity
    {
        [Key]
        public Guid MarksId { get; set; }

        [MaxLength(50)]
        public string? StudentID { get; set; } // String copy of ID

        [MaxLength(20)]
        public string? QuotaType { get; set; }

        [MaxLength(50)]
        public string? SeatNo { get; set; }

        [MaxLength(20)]
        public string? SemesterId { get; set; }
        [MaxLength(20)]
        public string? Pattern { get; set; }

        [MaxLength(255)]
        public string? OverallRemark { get; set; } // e.g., "Pass", "Fail"

        // Foreign Keys
        public Guid? StdMstId { get; set; }
        [ForeignKey(nameof(StdMstId))]
        public StudentMaster? Student { get; set; }

        public Guid? ExamId { get; set; }
        [ForeignKey(nameof(ExamId))]
        public ExamMaster? Exam { get; set; }

        // 🔹 Academic Year (GUID based)
        public Guid? AcademicYearAYID { get; set; }
        [ForeignKey(nameof(AcademicYearAYID))]
        public AcademicYear? AcademicYear { get; set; }

        // Navigation Properties
        public ICollection<StudentMarks>? StudentMarks { get; set; }
    }
}