namespace ExamAPI.DTOs
{
    public class StudentPromotionDto
    {
    }
    public class SingleStudentDataRequest
    {
        public string StudentId { get; set; }
        public Guid Ayid { get; set; }
    }
    public class SingleStudentData
    {
        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public string Semester { get; set; }
        public string Branch { get; set; }
        public string AcademicYear { get; set; }
    }
    public class EligibilityStudentsAssign
    {
        public Guid? CourseId { get; set; }
        public Guid Ayid { get; set; }
        public Guid PreviousAyid { get; set; }
        public string Semester { get; set; }
        public string Pattern { get; set; }
    }
    public class EligibilityAssignedStudent
    {
        public Guid? StdMstId { get; set; }
        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public bool Eligibility { get; set; }
        public string Credits { get; set; }
        public string CreditGradePoint { get; set; }
        public List<SemesterDetailsDto> SemesterDetails { get; set; }
       = new List<SemesterDetailsDto>();
    }
    public class EligibilityStudentResponse
    {
        public List<EligibilityAssignedStudent> AssignedStudents { get; set; } = new();

        public List<EligibilityAssignedStudent> UnassignedStudents { get; set; } = new();
    }
    public class EligibleStudents
    {
        public Guid StdMstId { get; set; }
        public string StudentID { get; set; }
        public bool IsEligible { get; set; }
    }
    public class UpdateEligibleStudent
    {
        public Guid StdMstId { get; set; }
        public string StudentID { get; set; }
        public bool Eligibility { get; set; }
    }

    public class SaveEligibility
    {
        public EligibilityStudentsAssign ExamInfo { get; set; }

        public List<EligibleStudents> Stduents { get; set; }
    }
    public class UpdateEligibility
    {
        public EligibilityStudentsAssign ExamInfo { get; set; }

        public List<UpdateEligibleStudent> Stduents { get; set; }
    }
    public class SemesterDetailsDto
    {
        public string Semester { get; set; }
        public decimal? Credit { get; set; }
        public decimal? CreditGradePoint { get; set; }
        public decimal? CGPI { get; set; }
        public decimal? SGPI { get; set; }
    }
}
