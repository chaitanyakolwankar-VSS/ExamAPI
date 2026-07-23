using System.ComponentModel.DataAnnotations;

namespace ExamAPI.Models
{
    public abstract class BaseEntity
    {
        public Guid? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public bool IsDeleted { get; set; } 

        public DateTime? DeletedAt { get; set; }
    }
}
