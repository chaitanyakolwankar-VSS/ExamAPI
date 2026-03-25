namespace ExamAPI.DTOs
{
    public class RegularExamDto
    {
    }
    public class RegularExamResponse
    {
        public Guid ExamId { get; set; }
        public string Examname { get; set; }
    }
    public class CheckCredits
    {
        public List<Guid> SubjectIds { get; set; } = new();
        public string? Ayid { get; set; }
    }

    public class RegularStudents
    {
        public Guid StdMstId { get; set; }
        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public bool Assigned { get; set; }
    }
    public class RegularAssignedStudents
    {
        public Guid StdMstId { get; set; }
        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public bool Assigned { get; set; }
   
    }
    public class RegularStudentResponse
    {
        public List<RegularAssignedStudents> AssignedStudents { get; set; }
        public List<RegularStudents> UnassignedStudents { get; set; }
    }

    public class RegularExamStudents
    {
        public Guid CourseId { get; set; }
        public string Pattern { get; set; }
        public string Semester { get; set; }
        public Guid ExamId { get; set; }
        public List<Guid> SubjectId { get; set; }
        public Guid Ayid { get; set; }
    }
    public class SaveRegularExamStudentsDto
    {
        // Master data
        public RegularExamStudents ExamInfo { get; set; }

        // Child list
        public List<RegularStudents> Students { get; set; }
    }
}
