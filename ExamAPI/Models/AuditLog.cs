using System.ComponentModel.DataAnnotations;

namespace ExamAPI.Models
{
    public class AuditLog
    {
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