namespace ExamAPI.DTOs
{
    public class GenerateHallTicketDto
    {
    }
    public class HallTicketSubjectsRequest
    {
        public string Ayid { get; set; }
        public Guid CourseId { get; set; }
        public string Semester { get; set; }
        public string Pattern { get; set; }
        public Guid ExamId { get; set; }
    }
    public class HallTicketSubjects
    {
        public Guid SubjectId { get; set; }
        public string SubjectCode { get; set; }
        public string SubjectName { get; set; }
        public string ExamTime { get; set; }
        public string ExamDate { get; set; }
    }
    public class SaveTimeTable
    {
        public Guid ExamId { get; set; }
        public Guid CourseId { get; set; }
        public List<HallTicketSubjects> TimeTableData { get; set; }

    }
    public class StudentsHallTicketSubjects
    {
        public string code { get; set; }
        public string name { get; set; }
        public string date { get; set; }
        public string time { get; set; }
    }
    public class StudentHallTicketData
    {
        public string name { get; set; }
        public string centre { get; set; }
        public string seat { get; set; }
        public string Studentid { get; set; }
        public List<StudentsHallTicketSubjects> subjects { get; set; }
    }
    public class StudentHallTicketDataRequest
    {
        public Guid Ayid {  get; set; }
        public Guid ExamId { get; set; }
        public string Semester { get; set; }
        public string Pattern { get; set; }
        public string Mode { get; set; }
        public string? StudentId { get; set; }
    }
    public class HallTicketCollege
    {
        public  string Logo { get; set; }
        public string Center { get; set; }
    }
}
