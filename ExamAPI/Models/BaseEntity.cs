using System.ComponentModel.DataAnnotations;

namespace ExamAPI.Models
{
    public abstract class BaseEntity
    {
        [MaxLength(100)]
        public Guid? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public bool IsDeleted { get; set; } 

        public DateTime? DeletedAt { get; set; }
    }
}
