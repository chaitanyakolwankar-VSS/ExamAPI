using System;
using System.ComponentModel.DataAnnotations;

namespace ExamAPI.DTOs
{
    // --- Pattern DTOs ---
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

    // --- RuleSet DTOs ---
    public class RuleSetDto
    {
        public Guid RuleSetId { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
        public Guid PatternId { get; set; }
    }

    public class RuleSetCreateDto
    {
        [Required]
        public string Name { get; set; }
        public bool IsActive { get; set; } = true;
        [Required]
        public Guid PatternId { get; set; }
    }

    public class RuleSetUpdateDto
    {
        [Required]
        public Guid RuleSetId { get; set; }
        [Required]
        public string Name { get; set; }
        public bool IsActive { get; set; }
    }
}