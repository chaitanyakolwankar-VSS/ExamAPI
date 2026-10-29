using System.ComponentModel.DataAnnotations;

namespace ExamAPI.Models
{
    /// <summary>
    /// An exam period's supervision plan ("May–June 2026 End-Semester"): which staff are on duty on which day and
    /// session, and in what role. Every semester's exams in the period share one chart, and each person gets one
    /// supervision letter for the whole chart. Replaces the hand-made duty chart Excel (which can still be imported).
    /// </summary>
    public class DutyChart : BaseEntity, ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }
        public Guid? CollegeId { get; set; }

        /// <summary>The academic year; letter numbers run per year.</summary>
        public Guid AYID { get; set; }

        [MaxLength(200)]
        public required string Name { get; set; }
        /// <summary>"First Half", "Second Half" or "" (printed on the letters).</summary>
        [MaxLength(20)]
        public string Half { get; set; } = "";
        /// <summary>Date only.</summary>
        public DateTime FromDate { get; set; }
        /// <summary>Date only.</summary>
        public DateTime ToDate { get; set; }
        /// <summary>A line for the staff, shown on the chart (e.g. "As per the University we have to be present in campus for 8 hours").</summary>
        [MaxLength(500)]
        public string? Note { get; set; }

        public ICollection<DutyChartDay> Days { get; set; } = new List<DutyChartDay>();
        public ICollection<DutyChartEntry> Entries { get; set; } = new List<DutyChartEntry>();
    }

    /// <summary>One exam day of a chart with its session times (filled from the timetables, editable). A null time = no such session that day.</summary>
    public class DutyChartDay : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }
        public Guid? CollegeId { get; set; }

        public Guid ChartId { get; set; }
        public DutyChart? Chart { get; set; }

        /// <summary>Date only.</summary>
        public DateTime Date { get; set; }
        /// <summary>e.g. "10:30 AM - 1:30 PM"; null = no morning session.</summary>
        [MaxLength(50)]
        public string? Morning { get; set; }
        /// <summary>e.g. "2:30 PM - 5:30 PM"; null = no afternoon session.</summary>
        [MaxLength(50)]
        public string? Afternoon { get; set; }
        /// <summary>The day (and its times) came from a timetable; false = added or changed by hand.</summary>
        public bool FromTimetable { get; set; }
    }

    /// <summary>What one person does on one day of a chart: a duty (session + role), valuation, or other work.</summary>
    public class DutyChartEntry : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }
        public Guid? CollegeId { get; set; }

        public Guid ChartId { get; set; }
        public DutyChart? Chart { get; set; }
        public Guid StaffId { get; set; }
        public StaffMaster? Staff { get; set; }

        /// <summary>Date only.</summary>
        public DateTime Date { get; set; }
        /// <summary>DutyKinds.</summary>
        [MaxLength(20)]
        public string Kind { get; set; } = DutyKinds.Duty;
        /// <summary>DutySessions, for a duty; null otherwise.</summary>
        [MaxLength(5)]
        public string? Session { get; set; }
        /// <summary>SupervisionRoles, for a duty; null otherwise.</summary>
        [MaxLength(40)]
        public string? Role { get; set; }
        /// <summary>Free text for "other" (e.g. "Exam cell"), shown on the chart and the letter.</summary>
        [MaxLength(100)]
        public string? Note { get; set; }
    }

    /// <summary>A person on a chart's list: the people who are on duty in the period. A chart starts with nobody. Everyone with an entry is a member; a member without entries gets no letter.</summary>
    public class DutyChartMember : ICollegeScoped
    {
        [Key]
        public Guid Id { get; set; }
        public Guid? CollegeId { get; set; }

        public Guid ChartId { get; set; }
        public DutyChart? Chart { get; set; }
        public Guid StaffId { get; set; }
        public StaffMaster? Staff { get; set; }

        /// <summary>Position on the chart, 0 = first.</summary>
        public int SortOrder { get; set; }
    }

    public static class DutyKinds
    {
        public const string Duty = "duty";
        public const string Valuation = "valuation";
        public const string Other = "other";

        public static readonly IReadOnlyList<string> All = new[] { Duty, Valuation, Other };
    }

    public static class DutySessions
    {
        public const string Morning = "M";
        public const string Afternoon = "A";
        public const string Both = "MA";

        public static readonly IReadOnlyList<string> All = new[] { Morning, Afternoon, Both };
    }
}
