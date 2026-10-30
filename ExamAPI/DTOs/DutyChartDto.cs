using Microsoft.AspNetCore.Http;

namespace ExamAPI.DTOs
{
    /// <summary>A duty chart in the list. Dates are yyyy-MM-dd.</summary>
    public class DutyChartSummaryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        /// <summary>"First Half", "Second Half" or empty.</summary>
        public string Half { get; set; } = "";
        public string FromDate { get; set; } = "";
        public string ToDate { get; set; } = "";
        public string? Note { get; set; }
        public int DayCount { get; set; }
        /// <summary>People on the chart (its members).</summary>
        public int StaffCount { get; set; }
        /// <summary>Duty sessions: a morning or an afternoon counts one, both count two.</summary>
        public int DutyCount { get; set; }
        /// <summary>Supervision letters of the chart.</summary>
        public int LetterCount { get; set; }
    }

    /// <summary>Create or edit a chart. Dates are yyyy-MM-dd.</summary>
    public class DutyChartSave
    {
        /// <summary>The academic year; cannot be changed once the chart exists.</summary>
        public Guid Ayid { get; set; }
        public string? Name { get; set; }
        /// <summary>"First Half", "Second Half" or empty.</summary>
        public string? Half { get; set; }
        public string? FromDate { get; set; }
        public string? ToDate { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>One exam day of a chart. A null time = no such session that day.</summary>
    public class DutyChartDayDto
    {
        /// <summary>yyyy-MM-dd.</summary>
        public string Date { get; set; } = "";
        public string Weekday { get; set; } = "";
        public string? Morning { get; set; }
        public string? Afternoon { get; set; }
        /// <summary>The day and its times came from a timetable; false = added or changed by hand.</summary>
        public bool FromTimetable { get; set; }
        /// <summary>Different papers timetabled that day.</summary>
        public int Papers { get; set; }
    }

    /// <summary>What one person does on one day: a duty (session and role) or other work. Also the shape the screen sends.</summary>
    public class DutyChartEntryDto
    {
        public Guid StaffId { get; set; }
        /// <summary>yyyy-MM-dd.</summary>
        public string Date { get; set; } = "";
        /// <summary>duty or other.</summary>
        public string Kind { get; set; } = "";
        /// <summary>M, A or MA, for a duty; null otherwise.</summary>
        public string? Session { get; set; }
        public string? Role { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>A person on the chart. The list is in chart order.</summary>
    public class DutyChartMemberDto
    {
        public Guid StaffId { get; set; }
        public int SortOrder { get; set; }
    }

    public class DutyLineDto
    {
        /// <summary>yyyy-MM-dd.</summary>
        public string Date { get; set; } = "";
        public string Time { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Role { get; set; } = "";
    }

    public class DutyChartLetterDto
    {
        public Guid LetterId { get; set; }
        public Guid StaffId { get; set; }
        public string StaffName { get; set; } = "";
        public string LetterNo { get; set; } = "";
        /// <summary>yyyy-MM-dd.</summary>
        public string LetterDate { get; set; } = "";
        public string Role { get; set; } = "";
        public List<DutyLineDto> Lines { get; set; } = new();
    }

    /// <summary>Everything the Duty Chart screen needs for one chart.</summary>
    public class DutyChartDetailDto
    {
        public DutyChartSummaryDto Chart { get; set; } = new();
        public List<DutyChartDayDto> Days { get; set; } = new();
        /// <summary>The people on the chart, in chart order. A new chart has nobody.</summary>
        public List<DutyChartMemberDto> Members { get; set; } = new();
        public List<DutyChartEntryDto> Entries { get; set; } = new();
        public List<string> Roles { get; set; } = new();
        public List<DutyChartLetterDto> Letters { get; set; } = new();
    }

    /// <summary>A day with its session times, as the screen sends the day list. An empty time = no such session.</summary>
    public class DutyChartDaySave
    {
        public string? Date { get; set; }
        public string? Morning { get; set; }
        public string? Afternoon { get; set; }
    }

    /// <summary>The whole chart: people and days missing from it lose their entries, and letters follow.</summary>
    public class DutyChartEntriesSave
    {
        /// <summary>yyyy-MM-dd; given to the new letters only.</summary>
        public string? LetterDate { get; set; }
        /// <summary>The people on the chart, in order. Anyone with an entry who is missing here is added at the end. Null = the people with entries.</summary>
        public List<Guid>? Members { get; set; }
        public List<DutyChartEntryDto> Entries { get; set; } = new();
    }

    public class DutyChartSaveResult
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int Removed { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>The filled-in chart sheet. Answered with a StaffImportResult.</summary>
    public class DutyChartImportRequest
    {
        public IFormFile? File { get; set; }
        /// <summary>Check the file and show what would change, without saving.</summary>
        public bool Preview { get; set; }
        /// <summary>yyyy-MM-dd; given to the new letters only. Today when empty.</summary>
        public string? LetterDate { get; set; }
    }
}
