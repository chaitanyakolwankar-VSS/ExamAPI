namespace ExamAPI.DTOs
{
    public class DeclareResultDTO
    {
        public Guid ExamId { get; set; }
        public string Examname { get; set; }
        public string Semester { get; set; }
        public Guid Ayid { get; set; }
        public Guid CourseId { get; set; }
        public bool IsDeclare { get; set; }
        public DateTime? DeclareDate { get; set; }
        public string Pattern { get; set; }
    }

    public class GetDeclareExam
    {
        public Guid CourseId { get; set; }
        public Guid Ayid { get; set; }
        public string Semester { get; set; }
        public string Pattern { get; set; }
    }

    public class DeclareExamTable
    {
        public Guid CourseId { get; set; }
        public Guid Ayid { get; set; }
        public string Semester { get; set; }
        public Guid ExamId { get; set; }
        public DateTime? DeclareDate { get; set; }
        public string Pattern { get; set; }
    }


    public class ToggleDeclareResultDTO
    {
        public Guid ExamId { get; set; }
        public Guid CourseId { get; set; }
        public Guid Ayid { get; set; }
        public string Semester { get; set; }
        public DateTime DeclareDate { get; set; }
        public bool IsDeclare { get; set; }
        public string Pattern { get; set; }
    }
}
