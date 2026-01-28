using System.ComponentModel.DataAnnotations;

namespace ExamAPI.DTOs
{
    public class OrdinanceDto
    {
    }
    public class PatternUpdateDto
    {
        [Required]
        public Guid PatternId { get; set; }
        [Required]
        public string PatternName { get; set; }
        public string Description { get; set; }
    }
    public class PatternCreateDto
    {
        public string PatternName { get; set; }
        public string Description { get; set; }
    }

    public class PatternDto
    {
        public Guid PatternId { get; set; }
        public string PatternName { get; set; }
        public string Description { get; set; }
    }
}
