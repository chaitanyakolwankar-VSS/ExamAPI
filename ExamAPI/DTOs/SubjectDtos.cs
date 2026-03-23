using System.ComponentModel.DataAnnotations;

namespace ExamAPI.DTOs
{
    public class SubjectDtos
    {
        public Guid SubjectId { get; set; }
        public string SubjectName { get; set; }
    }
    public class GetSubjectReqDtos
    {
        public Guid CourseId { get; set; }
        public string Pattern { get; set; }
        public string Semester { get; set; }
    }
    public class CreateSubjectDto
    {
        [Required]
        [MaxLength(100)]
        public string SubjectName { get; set; }

        [Required]
        [MaxLength(20)]
        public string SubjectCode { get; set; }
        [Required]
        [MaxLength(20)]

        public string SemId { get; set; }
        [Required]
        [MaxLength(50)]
        public string SemName { get; set; }
        [Required]
        [MaxLength(50)]
        public string Pattern { get; set; }
        public Guid CourseID { get; set; }
    }
    public class CreditDto
    {
        public Guid? CreditId { get; set; }
        [Required]
        public string CreditNo { get; set; }

        public List<string> ExamType { get; set; }
        public List<string> InternalType { get; set; }

        public string ExamOutOf { get; set; }
        public string ExamPassing { get; set; }

        public string InternalOutOf { get; set; }
        public string InternalPassing { get; set; }

        public string PassingPercentage { get; set; }
    }
    public class SaveCreditsDto
    {
        public Guid SubjectId { get; set; }
        public string Ayid { get; set; }
        public List<CreditDto> Credits { get; set; }
    }
    public class GetCredits
    {
        public Guid SubjectId { get; set; }

        public string Ayid { get; set; }
    }
    public class DeleteCreditsDto
    {
        public Guid? CreditId { get; set; }
    }
    public class DeleteCreditDto
    {
        public Guid SubjectId { get; set; }
        public string Ayid { get; set; }
        public List<DeleteCreditsDto> Credits { get; set; }
    }
    public class DeleteSubjectDto
    {
        public Guid SubjectId { get; set; }
    }
    public class PreviousCredits
    {
        public Guid SubjectId { get; set; }
        public string Ayid { get; set; }
        public string PreAyid { get; set; }
    }
    public class VerifyCreditAccessDto
    {
        public string Password { get; set; }
    }

}
