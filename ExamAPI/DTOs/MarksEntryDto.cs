using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ExamAPI.Models;

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

        /// <summary>"HeadWise" or "Combined" -- drives whether the grid colours per head or per subject.</summary>
        public string PassingStrategy { get; set; } = PassingStrategies.HeadWise;

        /// <summary>Combined pass threshold as a percentage of the subject total. Null for head-wise.</summary>
        public int? PassPercentage { get; set; }

        public List<StudentHeadMarksDto> Heads { get; set; } = new();
    }

    public class StudentHeadMarksDto
    {
        public Guid StudentMarksId { get; set; }
        public Guid CreditId { get; set; }

        /// <summary>The configured head row this mark belongs to; the key resolution config is stored against.</summary>
        public Guid SubjectCreditId { get; set; }

        public string HeadName { get; set; } // display label, e.g. "ESE"
        public string? Marks { get; set; } // Can be "Ab" or numeric
        public int OutOf { get; set; }
        public int Passing { get; set; }
        public string? Grace { get; set; }
        public bool IsAbsent { get; set; }

        /// <summary>Derived, never stored: whether this head clears its own passing marks.</summary>
        public bool IsPassed { get; set; }

        /// <summary>Condonation limit configured for this head on this exam, if any.</summary>
        public int? Resolution { get; set; }

        public bool IsEnabled { get; set; } // Based on HMCheck or other logic
    }

    public class SaveMarksRequest
    {
        public List<StudentMarksUpdateDto> Updates { get; set; } = new();
        public int Rank { get; set; }

        /// <summary>The subject being entered. Needed to apply resolution across a subject's
        /// heads even when only the resolution limits changed and no mark did.</summary>
        public Guid ExamId { get; set; }
        public Guid SubjectId { get; set; }

        /// <summary>Resolution limits set on the marks-entry screen; upserted into ResolutionMaster on save.</summary>
        public List<HeadResolutionDto> Resolutions { get; set; } = new();
    }

    public class StudentMarksUpdateDto
    {
        public Guid StudentMarksId { get; set; }
        public string? Marks { get; set; }
    }

    public class HeadResolutionDto
    {
        public Guid SubjectCreditId { get; set; }
        public int? Resolution { get; set; }
    }
}
