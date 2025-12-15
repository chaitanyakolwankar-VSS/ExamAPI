using System.ComponentModel.DataAnnotations;

namespace ExamAPI.Models
{
    public class College : BaseEntity
    {
        [Key]
        public Guid CollegeId { get; set; }

        [Required]
        [MaxLength(50)]
        public required string Name { get; set; }

        [Required]
        [MaxLength(20)]
        public required string CollegeCode { get; set; }

        [Required]
        [MaxLength(100)]
        public required string CollegeCenter { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        [MaxLength(500)]
        public string? LogoUrl { get; set; }

        [MaxLength(500)]
        public string? LogoBannerUrl { get; set; }

        [Required]
        [MaxLength(255)]
        [EmailAddress]
        public required string ContactEmail { get; set; }

        [Required]
        [MaxLength(20)]
        [Phone]
        public required string ContactPhone { get; set; }


        // Navigation Properties
        public ICollection<AcademicYear>? AcademicYears { get; set; }
        public ICollection<UserMaster>? Users { get; set; }
        public ICollection<StudentMaster>? Students { get; set; }
        public ICollection<CourseMaster>? Courses { get; set; }
        public ICollection<PatternMaster>? Patterns { get; set; }
        public ICollection<GraceLookup>? GraceLookups { get; set; }
    }
}