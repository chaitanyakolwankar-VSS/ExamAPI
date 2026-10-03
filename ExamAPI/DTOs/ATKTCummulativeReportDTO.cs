namespace ExamAPI.DTOs
{
    public class ATKTCummulativeReportDTO
    {
    }
    public class ReportExamRequest
    {
        public Guid Ayid { get; set; }
        public Guid CourseId { get; set; }
    }
    public class ReportExamResponse
    {
        public Guid ExamId { get; set; }
        public string Examname { get; set; }
    }
    public class ExamRequestBase
    {
        public Guid Ayid { get; set; }
        public Guid ExamId { get; set; }
        public string Semester { get; set; }
        public string Pattern { get; set; }
    }

    public class HeadType : ExamRequestBase
    {
    }

    public class HeadTypeResponse
    {
        public string HeadType { get; set; }
    }

    public class AtktReportRequest : ExamRequestBase
    {
        public string HeadType { get; set; }
    }
    public class AtktReportResponse
    {
        public string SubjectName { get; set; }
        public string SeatNo { get; set; }
        public string SubjectCriteria { get; set; }
    }
}
