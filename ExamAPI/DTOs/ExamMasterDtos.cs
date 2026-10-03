namespace ExamAPI.DTOs
{
    public class ExamMasterDtos
    {
    }
    public class Exams
    {
        public Guid Courseid { get; set; }
        public string Name { get; set; }
        public string ExamType { get; set; }
        public bool RevalExam { get; set; }
        public Guid Ayid { get; set; }
    }
    public class GetExam
    {
        public Guid Courseid { get; set; }
        public Guid Ayid { get; set; }
    }
    public class GetExamResponse
    {
        public Guid ExamId { get; set; }
        public string Name { get; set; }
        public string ExamType { get; set; }
        public bool? IsActive { get; set; }
    }
    public class UpdateExam
    {
        public Guid ExamId { get; set; }
        public bool? ActiveStatus { get; set; }
    }
    public class DeleteExam
    {
        public Guid ExamId { get; set; }
    }
}
