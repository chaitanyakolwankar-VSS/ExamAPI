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

    // New DTOs for Dashboard Stats
    public class DashboardStatsDTO
    {
        public int TotalStudents { get; set; }
        public decimal PassPercentage { get; set; }
        public int TotalExamsConducted { get; set; }
        public int ATKTStudentCount { get; set; }
        public List<CourseStudentCountDTO> CourseStudentCounts { get; set; }
        public List<ExamLifecycleDTO> ExamLifecycle { get; set; }
    }

    public class ExamLifecycleDTO
    {
        public string ExamName { get; set; }
        public int AssignedStudent { get; set; }
        public int SeatNo { get; set; }
        public bool ReleaseHallTicket { get; set; }
        public int MarksEntered { get; set; }
        public int GazetteGnrt { get; set; }
        public bool IsDeclare { get; set; }
    }

    public class ExamTypeDistributionDTO
    {
        public string SemesterId { get; set; }
        public string ExamType { get; set; }
        public int Appeared { get; set; }
        public int Passed { get; set; }
    }
}
