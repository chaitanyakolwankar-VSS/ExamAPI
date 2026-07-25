using System.ComponentModel.DataAnnotations;

namespace ExamAPI.Models
{
    public class DeclareResult:BaseEntity
    {
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

    }
}
