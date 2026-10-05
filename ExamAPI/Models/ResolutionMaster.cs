using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class ResolutionMaster : ICollegeScoped
    {

        /// <summary>Tenant owner. See <see cref="ICollegeScoped"/>.</summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }
        [Key]
        public Guid ID { get; set; }
        public Guid ExamID { get; set; }
        public Guid? CreditID { get; set; }
        public Guid SubjectCreditID { get; set; }

        /// <summary>The resolution limit ('^') for this exam x head; 0 = off. One row per exam x head.</summary>
        public int Resolution { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        // Navigation Properties

        public virtual ExamMaster? Exam { get; set; }
        public virtual SubjectCreditMaster? Credit { get; set; }
        public virtual SubjectCredits? SubjectCredit { get; set; }
    }
}
