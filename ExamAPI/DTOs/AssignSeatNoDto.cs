namespace ExamAPI.DTOs
{
    public class AssignSeatNoDto
    {
    }
    public class GetAssignSeatNoExam
    {
        public Guid Courseid { get; set; }
        public Guid Ayid { get; set; }
        public string Semester { get; set; }
    }
    public class ExamResponse
    {
        public Guid ExamId { get; set; }
        public string Examname { get; set; }
    }
    public class GetAssignSeatNoStudents
    {
        public Guid CourseId { get; set; }
        public string Pattern { get; set; }
        public string Semester { get; set; }
        public Guid ExamId { get; set; }
        public Guid Ayid { get; set; }
    }
    public class AssignSeatNoStudents
    {
        public Guid MarksId { get; set; }
        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public string QuotaType { get; set; }
        public string SeatNo { get; set; }

    }
    public class SaveSeatNoRequest
    {
        public List<AssignSeatNoStudents> Students { get; set; }
    }

}
