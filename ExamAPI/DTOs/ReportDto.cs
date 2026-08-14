namespace ExamAPI.DTOs
{
    public class GazetteRequestDto
    {
        public Guid ExamId { get; set; }
        public string SemId { get; set; }
        public string Pattern { get; set; } = string.Empty;
        public Guid? GroupId { get; set; }
        
        // Formatting & Overrides
        public bool RoundNumber { get; set; } = true;
        public bool NoRleForFail { get; set; } = true;
        public bool CgpiForFail { get; set; } = false;
        public bool SgpiForFail { get; set; } = false;
        
        public bool MergeExam { get; set; } = false;
        public Guid? MergedExamId { get; set; }
        
        // Layout Config
        public int StudentsPerPage { get; set; } = 5;
        public int SubjectsPerRow { get; set; } = 7;
        
        // Calculations
        public List<int> CxgSems { get; set; } = new();
        public List<int> GpaSems { get; set; } = new List<int>();
        
        public string? FileName { get; set; }
    }

    public class GazetteReportDto
    {
        public string CollegeName { get; set; } = string.Empty;
        public string ProgramName { get; set; } = string.Empty;
        public string Semester { get; set; } = string.Empty;
        public string ExamName { get; set; } = string.Empty;
        public DateTime ResultDate { get; set; }
        public List<StudentResultSummaryDto> Students { get; set; } = new();
        public bool ShowCgpi { get; set; }
    }

    public class MarksheetReportDto
    {
        /// <summary>Printed at the head of the marksheet; resolved from the College entity.</summary>
        public string CollegeName { get; set; } = string.Empty;

        public string StudentName { get; set; } = string.Empty;
        public string SeatNo { get; set; } = string.Empty;
        public string PRN { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        
        public string ProgramName { get; set; } = string.Empty;
        public string ExamName { get; set; } = string.Empty;
        public string Semester { get; set; } = string.Empty;
        public DateTime ResultDate { get; set; }

        public List<SubjectMarksDto> Subjects { get; set; } = new();
        
        public double TotalObtained { get; set; }
        public double TotalMax { get; set; }
        public double TotalCredits { get; set; }
        public double CreditsEarned { get; set; }
        
        public double SGPI { get; set; }
        public double? CGPI { get; set; }
        public double? CumulativeGrade { get; set; }
        
        public string Remark { get; set; } = string.Empty; // SUCCESSFUL, UNSUCCESSFUL, RLE
        
        // Final semester past records for the bottom table
        public List<SemesterRecordDto> PastSemesters { get; set; } = new();
    }

    public class StudentResultSummaryDto
    {
        public string StudentId { get; set; } = string.Empty;
        public string SeatNo { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string PRN { get; set; } = string.Empty;
        
        public List<SubjectMarksDto> Subjects { get; set; } = new();
        
        public double TotalObtained { get; set; }
        public double TotalMax { get; set; }
        public double TotalCredits { get; set; }
        public double CreditsEarned { get; set; }
        public double SGPI { get; set; }
        public double? CGPI { get; set; } 
        public double? CumulativeGrade { get; set; }
        public string Remark { get; set; } = string.Empty;
    }

    public class SubjectMarksDto
    {
        public string SubjectCode { get; set; } = string.Empty;
        public string SubjectName { get; set; } = string.Empty;
        public double Credits { get; set; }

        // Heads are configured by staff per subject.  A subject is not limited to
        // a theory/internal pair, so reports must preserve every configured head.
        public List<HeadMarksDto> Heads { get; set; } = new();

        public double TotalMax { get; set; }
        public double PassingMax { get; set; }
        public string TotalObtained { get; set; } = string.Empty;
        
        public string Grade { get; set; }

        /// <summary>Subject-level ordinance grace symbol; set only for combined subjects.</summary>
        public string? Grace { get; set; } = string.Empty;
        public int GradePoint { get; set; }
        public double EarnedGradePoints => Credits * GradePoint;
    }

    public class HeadMarksDto
    {
        public string Head { get; set; } = string.Empty;
        public double Max { get; set; }
        public string Marks { get; set; } = string.Empty;
        public string Grace { get; set; } = string.Empty;
    }

    public class SemesterRecordDto
    {
        public string SemesterName { get; set; } = string.Empty;
        public double Credits { get; set; }
        public double SGPI { get; set; }
        public double EarnedGradePoints { get; set; }
    }
}
