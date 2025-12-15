using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class StudentMaster : BaseEntity
    {
        [Key]
        public Guid StdMstId { get; set; }

        [Required]
        [MaxLength(50)]
        public required string StudentId { get; set; } // The readable ID (e.g., "STD2023001")

        [Required]
        [MaxLength(50)]
        public required string FirstName { get; set; }

        [MaxLength(50)]
        public string? MiddleName { get; set; }

        [Required]
        [MaxLength(50)]
        public required string LastName { get; set; }

        [MaxLength(255)]
        [EmailAddress]
        public string? Email { get; set; }

        [MaxLength(20)]
        [Phone]
        public string? Phone { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        [MaxLength(10)]
        public string? Gender { get; set; }

        [MaxLength(50)]
        public string? Category { get; set; } // e.g., "Open", "OBC", "SC"

        [MaxLength(50)]
        public string? StudentPRN { get; set; } // Permanent Registration Number

        [MaxLength(500)]
        public string? PhotoUrl { get; set; }

        [MaxLength(500)]
        public string? SignUrl { get; set; }

        public DateTime? DateOfBirth { get; set; }

        // Foreign Key
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? College { get; set; }


        // Navigation Properties
        public ICollection<StudentEligibility>? Eligibilities { get; set; }
        public ICollection<MarksMaster>? Marks { get; set; }
        public ICollection<StudentsOverallResult>? OverallMarks { get; set; }
    }
}