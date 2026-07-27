using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class MarksMaster : BaseEntity, ICollegeScoped
    {

        /// <summary>Tenant owner. See <see cref="ICollegeScoped"/>.</summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }
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

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? SGPI { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? CGPI { get; set; }

        [MaxLength(50)]
        public string? ResultRemark { get; set; } // e.g., "RLE"

        public bool? HMCheck { get; set; }

        public int? Rank { get; set; }

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