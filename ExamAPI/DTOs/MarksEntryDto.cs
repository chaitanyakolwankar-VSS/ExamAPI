using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamAPI.DTOs
{
    public class MarksEntryFilterRequest
    {
        [Required]
        public Guid BranchId { get; set; }
        [Required]
        public string SemId { get; set; }
        [Required]
        public string Pattern { get; set; }
        [Required]
        public Guid ExamId { get; set; }
        [Required]
        public Guid SubjectId { get; set; }
        public string? StudentId { get; set; }
    }

    public class MarksEntryDataDto
    {
        public Guid MarksId { get; set; }
        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public string SeatNo { get; set; }
        public int Rank { get; set; }
        public List<StudentHeadMarksDto> Heads { get; set; } = new();
    }

    public class StudentHeadMarksDto
    {
        public Guid StudentMarksId { get; set; }
        public Guid CreditId { get; set; }
        public string HeadName { get; set; } // e.g., "Theory"
        public string? Marks { get; set; } // Can be "Ab" or numeric
        public int OutOf { get; set; }
        public int Passing { get; set; }
        public string? Grace { get; set; }
        public string? Remark { get; set; }
        public bool IsEnabled { get; set; } // Based on HMCheck or other logic
    }

    public class SaveMarksRequest
    {
        public List<StudentMarksUpdateDto> Updates { get; set; } = new();
        public int Rank { get; set; }
    }

    public class StudentMarksUpdateDto
    {
        public Guid StudentMarksId { get; set; }
        public string? Marks { get; set; }
    }
}
