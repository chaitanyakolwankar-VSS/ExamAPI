namespace ExamAPI.DTOs
{
    public class AcademicYearDto
    {
        public Guid AYID { get; set; }
        public required string ShortDuration { get; set; }  // e.g. "2024-2025"
        public bool IsCurrent { get; set; }
    }
}