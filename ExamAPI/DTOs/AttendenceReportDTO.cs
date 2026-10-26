namespace ExamAPI.DTOs
{
    public class AttendenceReportDTO
    {
    }
    public class AttendenceReportRequestDTO
    {
        public Guid Ayid { get; set; }
        public Guid CourseId { get; set; }
        public string Pattern { get; set; }
        public string Semester { get; set; }
        public Guid Exam { get; set; }

    }
    public class AttendenceReportResponseDTO
    {
        public Guid SubjectId { get; set; }
        public string SubjectCode { get; set; } = string.Empty;
        public string SubjectName { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public int StudentCount { get; set; }
    }

    public class AttendenceSeatNoRequestDTO:AttendenceReportRequestDTO
    {

        public Guid SubjectId { get; set; }

    }
    public class AttendenceSeatNoResponseDTO
    {
        public string SeatNo { get; set; } = string.Empty;
    }

}
