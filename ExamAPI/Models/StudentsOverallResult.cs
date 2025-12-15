using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class StudentsOverallResult : BaseEntity
    {
        [Key]
        public Guid Id { get; set; }

        [MaxLength(20)]
        public string? SemesterId { get; set; }

        [MaxLength(20)]
        public string? CreditGradePoint { get; set; }

        [MaxLength(20)]
        public string? Credits { get; set; }

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