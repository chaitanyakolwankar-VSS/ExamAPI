using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamAPI.Models
{
    /// <summary>Allowed values for <see cref="ExamAssignmentPolicy.Mode"/>.</summary>
    public static class AssignmentModes
    {
        /// <summary>Allowed To Keep Term -- the backlog/supplementary attempt.</summary>
        public const string Atkt = "ATKT";

        /// <summary>Revaluation of an already-conducted exam.</summary>
        public const string Revaluation = "Revaluation";

        public static bool IsRevaluation(string? mode) =>
            string.Equals(mode, Revaluation, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The data-driven configuration behind the ATKT / Revaluation exam-assignment screen.
    /// <para>
    /// Everything the legacy WebForm (frm_atktreval_exm_assign.aspx) hard-coded -- which exam
    /// types may be a source or a target, whether a passed subject can be re-registered,
    /// whether an absent student may apply for revaluation, which heads are revaluable, how
    /// many subjects one student may carry -- lives here as a row instead of in code. A
    /// college changes its policy by editing data; no redeploy.
    /// </para>
    /// <para>
    /// Every policy belongs to exactly one college -- there are no platform-wide rows. A
    /// college with no policy of its own falls back to the built-in defaults in
    /// AtktRevalExamService, which are the behaviour the legacy form hard-coded.
    /// </para>
    /// </summary>
    public class ExamAssignmentPolicy : BaseEntity, ICollegeScoped
    {
        /// <summary>
        /// Tenant owner. Nullable on the CLR type because <see cref="ICollegeScoped"/> requires
        /// it, but the column is NOT NULL -- see the IsRequired() call in OnModelCreating.
        /// SaveChangesAsync stamps it on insert, so callers never set it.
        /// </summary>
        public Guid? CollegeId { get; set; }

        [ForeignKey(nameof(CollegeId))]
        public College? OwningCollege { get; set; }

        [Key]
        public Guid PolicyId { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        /// <summary>One of <see cref="AssignmentModes"/>.</summary>
        [MaxLength(20)]
        public string Mode { get; set; } = AssignmentModes.Atkt;

        /// <summary>
        /// Comma-separated <see cref="ExamMaster.ExamType"/> values that may act as the SOURCE
        /// exam (the attempt whose result decides who is eligible). Empty means "any type".
        /// Replaces the legacy <c>exam_code like 'E%'</c> filter.
        /// </summary>
        [MaxLength(200)]
        public string? SourceExamTypes { get; set; }

        /// <summary>
        /// Comma-separated <see cref="ExamMaster.ExamType"/> values that may act as the TARGET
        /// exam (the one students are being assigned into). Replaces the legacy
        /// <c>exam_code like 'EXM%' and atkt_exam=1</c> filter.
        /// </summary>
        [MaxLength(200)]
        public string? TargetExamTypes { get; set; }

        /// <summary>Only subjects the student did not clear may be selected (ATKT).</summary>
        public bool RequireFailedSubject { get; set; } = true;

        /// <summary>A subject the student already passed may still be selected (revaluation).</summary>
        public bool OfferPassedSubjects { get; set; }

        /// <summary>An absent student may not apply (revaluation -- there is nothing to revalue).</summary>
        public bool BlockAbsentStudents { get; set; }

        /// <summary>Pre-tick the failed subjects when the operator opens a fresh assignment.</summary>
        public bool AutoSelectFailedSubjects { get; set; } = true;

        /// <summary>
        /// Comma-separated <see cref="SubjectCredits.HeadType"/> values a subject must expose for
        /// it to be selectable, e.g. "ESE" for revaluation. Empty means "any head".
        /// Replaces the hard-coded <c>"ESE"</c> literal in the legacy grid renderer.
        /// </summary>
        [MaxLength(200)]
        public string? EligibleHeadTypes { get; set; }

        /// <summary>Cap on subjects one student may be assigned in a single attempt. Null/0 = no cap.</summary>
        public int? MaxSubjectsPerStudent { get; set; }

        /// <summary>Copy <see cref="MarksMaster.SeatNo"/> forward from the source attempt.</summary>
        public bool CarryForwardSeatNo { get; set; } = true;

        /// <summary>
        /// Create carry-forward <see cref="StudentMarks"/> rows for the subjects the student is
        /// NOT re-appearing for, holding the source marks. This is the modelled replacement for
        /// the legacy trailing-'+' sentinel inside the h1/h2 strings.
        /// </summary>
        public bool CarryForwardMarks { get; set; } = true;

        /// <summary>Refuse to unassign a student once any mark has been entered for the target exam.</summary>
        public bool BlockDeleteAfterMarksEntry { get; set; } = true;

        /// <summary>
        /// Optional ordinance rule set used as an eligibility gate. Every enabled rule in the set
        /// must evaluate true against the student's source attempt for that student to be listed.
        /// Facts come from the existing engine registry (FailedSubjectCount, SemesterNo, ...).
        /// </summary>
        public Guid? RuleSetId { get; set; }

        [ForeignKey(nameof(RuleSetId))]
        public RuleSet? RuleSet { get; set; }

        /// <summary>Restricts the policy to one pattern. Null = applies to every pattern.</summary>
        public Guid? PatternId { get; set; }

        [ForeignKey(nameof(PatternId))]
        public PatternMaster? Pattern { get; set; }

        public bool IsEnabled { get; set; } = true;

        /// <summary>Layout hint for the client grid -- the legacy screen wrapped at 7 subjects.</summary>
        public int SubjectsPerRow { get; set; } = 7;
    }
}
