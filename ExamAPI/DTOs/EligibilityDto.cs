namespace ExamAPI.DTOs
{
    public class EligibilityDto
    {
    }
    public class GetEligibilityStudents
    {
        public Guid Ayid { get; set; }
        public Guid CourseId { get; set; }
        public string Semester { get; set; }
    }

    public class SemesterData
    {
        public string CG { get; set; }
        public string Credit { get; set; }
        public string KT_Theory { get; set; }
        public string KT_Others { get; set; }
    }
    public class EligibilityStudents
    {
        public string SerialNo { get; set; }
        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public Dictionary<int, SemesterData> semesters { get; set; }
    }
}
