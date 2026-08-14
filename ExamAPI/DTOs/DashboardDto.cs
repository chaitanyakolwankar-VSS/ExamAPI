namespace ExamAPI.DTOs
{
    public class DashboardDto
    {
        public Guid Ayid { get; set; }
        public Guid CourseId { get; set; }

    }
    public class CourseStudentCountDTO
    {
        public Guid CourseId { get; set; }
        public string CourseName { get; set; }
        public int StudentCount { get; set; }
    }

    public class SemesterStudentCountDTO
    {
        public string SemesterId { get; set; }
        public int StudentCount { get; set; }
    }

    public class PassFailChartDTO
    {
        public string SemesterId { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
    }
    public class SemesterExamTypeCountDTO
    {
        public string SemesterId { get; set; }
        public string ExamType { get; set; }
        public int StudentCount { get; set; }
    }
}
