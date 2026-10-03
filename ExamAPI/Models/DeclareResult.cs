using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class DeclareResult : BaseEntity, ICollegeScoped
    {
        /// <summary>Tenant owner. See <see cref="ICollegeScoped"/>.</summary>
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }

        [Key]
        public Guid DeclareID { get; set; }
        public string Sem_id { get; set; }
        public bool IsDeclare { get; set; }
        public Guid CourseId { get; set; }
        public Guid AcademicYear { get; set; }
        public Guid ExamId { get; set; }
        public DateTime? DeclareDate { get; set; }
        public int GazetteGnrt { get; set; }
        public string Pattern { get; set; }
        public bool ReleaseHallTicket { get; set; }
        public DateTime? HallTicketDeclareDate { get; set; }
        public DateTime? HallTicketUpdatedAt { get; set; }
        public DateTime? GazetteDate { get; set; }
        public int ResDeclare { get; set; }
        public DateTime? ResDeclareDateTime { get; set; }
    }
}
