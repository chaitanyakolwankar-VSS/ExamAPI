using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class AuditLog : ICollegeScoped
    {

        /// <summary>Tenant owner. See <see cref="ICollegeScoped"/>.</summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }
        [Key]
        public Guid Id { get; set; }

        [MaxLength(50)]
        public string? Action { get; set; }      // "UPDATE" or "DELETE"

        [MaxLength(100)]
        public string? TableName { get; set; }   // e.g., "StudentMaster"

        [MaxLength(100)]
        public string? RecordId { get; set; }    // The ID of the item changed

        public Guid? PerformedBy { get; set; }   // The User's ID (Guid)

        [MaxLength(50)]
        public string? UserType { get; set; }    // "Student" or "Staff"

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}