namespace ExamAPI.DTOs
{
    public class StudentAssignRptDto
    {
        public Guid CourseId { get; set; } 
        public Guid AYID { get; set; } 
        public string Pattern { get; set; } = string.Empty;
        public string Semester { get; set; } = string.Empty;
        public Guid ExamId { get; set; }

    }

    public class StudentAssignRptResDto
    {
        public string SubjectCode { get; set; } = string.Empty;
        public string SubjectName { get; set; } = string.Empty;
        public string? StudentID { get; set; }
        public string? SeatNo { get; set; }
        public string Name { get; set; }=string.Empty;
    }

    public class StudentCreditRptResponseDto
    {
        public string SubjectCode { get; set; } = string.Empty;
        public string SubjectName { get; set; } = string.Empty;
        public string TotalCredits { get; set; }
        public string Head { get; set; } = string.Empty;
        public string HeadFormula { get; set; } = string.Empty;
        public string HeadType { get; set; } = string.Empty;
        public string HeadOutOf { get; set; }
        public string HeadPass { get; set; }
        public string StudentID { get; set; } = string.Empty;
        public string SeatNo { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }


}
