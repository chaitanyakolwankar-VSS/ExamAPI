namespace ExamAPI.DTOs
{
    public class DeclareResultDTO
    {
        public Guid ExamId { get; set; }
        public string Examname { get; set; }
        public string Semester { get; set; }
        public Guid Ayid { get; set; }
        public Guid CourseId { get; set; }
        public bool IsDeclare { get; set; }
        public DateTime? DeclareDate { get; set; }
        public string Pattern { get; set; }
        /// <summary>False when no DeclareResult row exists yet (the exam is shown as "not declared").</summary>
        public bool HasRecord { get; set; }
        /// <summary>Bulk marksheets were generated at least once; required before declaring.</summary>
        public bool MarksheetGenerated { get; set; }
        /// <summary>The gazette (PDF or Excel) was generated at least once.</summary>
        public bool GazetteGenerated { get; set; }
    }

    public class DeclareHallTicketDTO
    {
        public Guid ExamId { get; set; }
        public string Examname { get; set; }
        public string Semester { get; set; }
        public Guid Ayid { get; set; }
        public Guid CourseId { get; set; }
        public DateTime? HallTicketDeclareDate { get; set; }
        public DateTime? HallTicketUpdatedAt { get; set; }
        public string Pattern { get; set; }
        public bool ReleaseHallTicket { get; set; }
        /// <summary>False when no DeclareResult row exists yet (the hall ticket is shown as "not released").</summary>
        public bool HasRecord { get; set; }
    }

    public class GetDeclareExam
    {
        public Guid CourseId { get; set; }
        public Guid Ayid { get; set; }
        public string Semester { get; set; }
        public string Pattern { get; set; }
    }

    public class DeclareExamTable
    {
        public Guid CourseId { get; set; }
        public Guid Ayid { get; set; }
        public string Semester { get; set; }
        public Guid ExamId { get; set; }
        public DateTime? DeclareDate { get; set; }
        public string Pattern { get; set; }
    }

    public class ToggleReleaseHallTicketDTO
    {
        public Guid ExamId { get; set; }
        public Guid CourseId { get; set; }
        public Guid Ayid { get; set; }
        public string Semester { get; set; }
        public string Pattern { get; set; }
        public bool ReleaseHallTicket { get; set; }
        public DateTime? HallTicketDeclareDate { get; set; }
    }

    public class ToggleDeclareResultDTO
    {
        public Guid ExamId { get; set; }
        public Guid CourseId { get; set; }
        public Guid Ayid { get; set; }
        public string Semester { get; set; }
        public DateTime? DeclareDate { get; set; }
        public bool IsDeclare { get; set; }
        public string Pattern { get; set; }
    }
}
