using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamAPI.DTOs
{
    public class ProcessResultRequest
    {
        [Required]
        public Guid BranchId { get; set; }
        [Required]
        public string SemId { get; set; }
        [Required]
        public string Pattern { get; set; }
        [Required]
        public Guid ExamId { get; set; }
        public string? StudentId { get; set; }
        public bool IsSingleStudent { get; set; }
    }

    public class ResultDataDto
    {
        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public string SeatNo { get; set; }
        public decimal TotalMarks { get; set; }
        public decimal OutOf { get; set; }
        public decimal Percentage { get; set; }
        public decimal Sgpi { get; set; }
        public decimal Cgpi { get; set; }
        public string ResultStatus { get; set; }
        public string Remarks { get; set; }
        public Dictionary<string, string> SubjectMarks { get; set; } = new Dictionary<string, string>();
    }

    public class ExamOptionDto
    {
        public Guid ExamId { get; set; }
        public string ExamCode { get; set; }
        public string ExamName { get; set; }
    }
}
