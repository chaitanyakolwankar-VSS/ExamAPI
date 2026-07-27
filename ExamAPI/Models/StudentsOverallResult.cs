using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class StudentsOverallResult : BaseEntity, ICollegeScoped
    {

        /// <summary>Tenant owner. See <see cref="ICollegeScoped"/>.</summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }
        [Key]
        public Guid Id { get; set; }

        [MaxLength(20)]
        public string? SemesterId { get; set; }

        [MaxLength(20)]
        public string? CreditGradePoint { get; set; }

        [MaxLength(20)]
        public string? Credits { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? SGPI { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? CGPI { get; set; }

        [MaxLength(50)]
        public string? KtTheory { get; set; } // Count of KTs

        [MaxLength(50)]
        public string? KtOther { get; set; }

        // Foreign Key
        public Guid? StdMstId { get; set; }
        [ForeignKey(nameof(StdMstId))]
        public StudentMaster? Student { get; set; }
    }
}