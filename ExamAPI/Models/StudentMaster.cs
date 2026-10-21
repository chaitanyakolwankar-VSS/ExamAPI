using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class StudentMaster : BaseEntity, ICollegeScoped
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

        [MaxLength(50)]
        public string? MotherName { get; set; }

        /// <summary>E = Engineering, D = Diploma (direct second-year admission), M = MCA. Null for students saved before this was asked.</summary>
        [MaxLength(1)]
        public string? StudentType { get; set; }

        /// <summary>Stored trimmed and lower-case; unique per college. Student task links and OTPs go here.</summary>
        [MaxLength(255)]
        [EmailAddress]
        public string? Email { get; set; }

        /// <summary><see cref="EmailVerification"/>: Verified, or Flagged with <see cref="EmailFlagReason"/>. Null = not checked yet.</summary>
        [MaxLength(20)]
        public string? EmailStatus { get; set; }

        [MaxLength(200)]
        public string? EmailFlagReason { get; set; }

        /// <summary>An inactive student (left, cancelled admission) cannot open a task link or sign in with an OTP.</summary>
        public bool IsActive { get; set; } = true;

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

        /// <summary>Academic Bank of Credits ID: 12 digits, printed on the hall ticket.</summary>
        [MaxLength(12)]
        public string? AbcId { get; set; }

        [MaxLength(500)]
        public string? PhotoUrl { get; set; }

        [MaxLength(500)]
        public string? SignUrl { get; set; }

        public DateTime? DateOfBirth { get; set; }

        // Foreign Key
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? College { get; set; }

        [Column("DyslexiaStudent")]
        public bool Dyslexia { get; set; }
        //public Guid CourseId { get; set; }

        // Navigation Properties
        public ICollection<StudentEligibility>? Eligibilities { get; set; }
        public ICollection<CourseMaster>? CourseMasters { get; set; }
        public ICollection<MarksMaster>? Marks { get; set; }
        public ICollection<StudentsOverallResult>? OverallMarks { get; set; }
    }
}