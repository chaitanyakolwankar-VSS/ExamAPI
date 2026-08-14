using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    public class GraceLookup : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid GraceLookupId { get; set; }

        public int HeadMarksUpto { get; set; }
        public int GraceMarks { get; set; }

        // Foreign Key
        public Guid? CollegeId { get; set; }
        [ForeignKey(nameof(CollegeId))]
        public College? College { get; set; }
    }
}