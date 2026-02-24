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
    public class GetResolutionExam
    {
        public Guid Courseid { get; set; }
        public Guid Ayid { get; set; }
        public string Semester { get; set; }
        public string Pattern { get; set; }
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
    public class ResolutionExamResponse
    {
        public Guid ExamId { get; set; }
        public string Examname { get; set; }
    }
    public class GetCreditHeadResolutionReq
    {
        public Guid SubjectId { get; set; }
        public Guid ExamId { get; set; }
        public string Ayid { get; set; }
    }
    public class GetCreditHeadResolutionres
    {
        public Guid CreditsId { get; set; }
        public Guid H1SubjectCredit { get; set; }
        public string? H1OutOf { get; set; }
        public string? H1Pass { get; set; }
        public string? H1Type { get; set; }
        public string? H1Res { get; set; }
        public Guid? H2SubjectCredit { get; set; }
        public string? H2OutOf { get; set; }
        public string? H2Pass { get; set; }
        public string? H2Type { get; set; }
        public string? H2Res { get; set; }
    }
    public class SaveCreditHeadResolutionres
    {
        public Guid ExamId { get; set; }
        public Guid CourseId { get; set; }
        public Guid Ayid { get; set; }
        public List<GetCreditHeadResolutionres> Items { get; set; }
    }
}
