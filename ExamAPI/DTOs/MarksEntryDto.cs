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


        public bool IsEnabled { get; set; } // Based on HMCheck or other logic

        /// <summary>
        /// True when this head was carried forward from the source attempt by an ATKT/Revaluation
        /// assignment: the student is not appearing for it, so the mark is fixed and the cell is
        /// locked for entry. Always false for a normal (fresh) exam head.
        /// </summary>
        public bool IsCarryForward { get; set; }
    }

    public class SaveMarksRequest
    {
        public List<StudentMarksUpdateDto> Updates { get; set; } = new();
        public int Rank { get; set; }

        /// <summary>The exam being entered; used for the locked-exam check.</summary>
        public Guid ExamId { get; set; }

        /// <summary>Informational. Saving marks never applies resolution any more -- it is
        /// configured separately (see <see cref="SaveResolutionConfigRequest"/>) and derived when
        /// results are processed.</summary>
        public Guid SubjectId { get; set; }
    }

    public class StudentMarksUpdateDto
    {
        public Guid StudentMarksId { get; set; }
        public string? Marks { get; set; }
    }

    // ----------------------------------------------------------------------------------------
    // Resolution ('^') configuration. ResolutionMaster is the only source of truth; the limits
    // are applied by result processing, never by marks entry.
    // ----------------------------------------------------------------------------------------

    /// <summary>Same context filters the marks-entry screen uses to list an exam's students and subjects.</summary>
    public class ResolutionConfigRequest
    {
        [Required]
        public Guid BranchId { get; set; }
        [Required]
        public string SemId { get; set; } = string.Empty;
        [Required]
        public string Pattern { get; set; } = string.Empty;
        [Required]
        public Guid ExamId { get; set; }
    }

    public class ResolutionConfigDto
    {
        public Guid ExamId { get; set; }

        /// <summary>A locked exam cannot be reprocessed, so its resolution config is read-only.</summary>
        public bool IsLocked { get; set; }

        public List<ResolutionConfigSubjectDto> Subjects { get; set; } = new();
    }

    public class ResolutionConfigSubjectDto
    {
        public Guid SubjectId { get; set; }
        public string SubjectCode { get; set; } = string.Empty;
        public string SubjectName { get; set; } = string.Empty;

        /// <summary>"HeadWise" or "Combined".</summary>
        public string PassingStrategy { get; set; } = PassingStrategies.HeadWise;
        public int? PassPercentage { get; set; }

        /// <summary>Sum of the heads' out-of marks (the combined total).</summary>
        public int OutOfTotal { get; set; }

        /// <summary>Combined pass mark: PassPercentage of OutOfTotal, or the sum of head passing marks.</summary>
        public int RequiredToPass { get; set; }

        public List<ResolutionConfigHeadDto> Heads { get; set; } = new();

        /// <summary>Combined only: the head currently carrying the limit (the first with a limit above 0), if any.</summary>
        public Guid? SelectedHeadSubjectCreditId { get; set; }

        /// <summary>Students entered in this subject for the exam.</summary>
        public int StudentCount { get; set; }

        /// <summary>Students who fail the subject on their RAW marks (fully entered, not absent in every head).</summary>
        public int FailingCount { get; set; }

        /// <summary>Failing students who would be condoned by the SAVED limits ("would be condoned").</summary>
        public int WithinLimitCount { get; set; }

        /// <summary>Students holding a resolution bump (Resolution &gt; 0) after the last processing.</summary>
        public int AppliedCount { get; set; }

        /// <summary>Combined only: the subject deficit of every failing student that has no absent head
        /// (the only students resolution can help). Lets the UI preview a typed limit without a round trip.</summary>
        public List<int> Deficits { get; set; } = new();
    }

    public class ResolutionConfigHeadDto
    {
        public Guid SubjectCreditId { get; set; }

        /// <summary>Positional key, "H1"/"H2".</summary>
        public string Head { get; set; } = string.Empty;

        /// <summary>Display label, e.g. "ESE".</summary>
        public string HeadType { get; set; } = string.Empty;
        public int OutOf { get; set; }
        public int Passing { get; set; }

        /// <summary>Saved limit for this head; 0 = off (also when nothing has been saved).</summary>
        public int Limit { get; set; }

        /// <summary>Head-wise only: the shortfall of every student who fails this head on raw marks
        /// (not absent, marks entered), for previewing a typed limit.</summary>
        public List<int> Deficits { get; set; } = new();
    }

    public class SaveResolutionConfigRequest
    {
        public Guid ExamId { get; set; }
        public List<ResolutionLimitDto> Limits { get; set; } = new();
    }

    public class ResolutionLimitDto
    {
        public Guid SubjectCreditId { get; set; }

        /// <summary>Whole number &gt;= 0, no upper bound. Null or 0 switches resolution off for the head.
        /// Typed as decimal only so a fractional value can be rejected with a clear message.</summary>
        public decimal? Limit { get; set; }
    }
}
