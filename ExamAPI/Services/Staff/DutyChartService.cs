using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Report;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Staff
{
    public interface IDutyChartService
    {
        Task<List<DutyChartSummaryDto>> ListAsync(Guid ayid);
        Task<DutyChartDetailDto> GetAsync(Guid id);
        Task<DutyChartDetailDto> CreateAsync(DutyChartSave dto);
        Task<DutyChartDetailDto> UpdateAsync(Guid id, DutyChartSave dto);
        /// <summary>Cancels the chart and its letters; returns how many letters were cancelled.</summary>
        Task<int> DeleteAsync(Guid id);
        Task<DutyChartDetailDto> RefreshDaysAsync(Guid id);
        Task<DutyChartDetailDto> SaveDaysAsync(Guid id, List<DutyChartDaySave> days);
        Task<DutyChartSaveResult> SaveEntriesAsync(Guid id, DutyChartEntriesSave dto);
        Task<(byte[] Bytes, string FileName)> ExcelAsync(Guid id);
        Task<StaffImportResult> ImportAsync(Guid id, DutyChartImportRequest request);
    }

    /// <summary>
    /// Duty charts: the exam office's plan of who supervises on which day and session in an exam period. The days come
    /// from the timetables (and can be edited), the screen or an Excel sheet gives the whole chart at once, and every
    /// person with at least one duty gets one numbered supervision letter that follows the chart. A letter number is
    /// never reused, even when the letter is cancelled.
    /// </summary>
    public class DutyChartService : IDutyChartService
    {
        private const string LetterDateFormat = "yyyy-MM-dd";
        private const string DayTextFormat = "dd-MM-yyyy";
        private const string FullDay = "Full day";
        private const string ValuationRole = "Valuation";
        private const string OtherRole = "Other work";
        private const int MaxDays = 366;

        private readonly ApplicationDbContext _context;

        public DutyChartService(ApplicationDbContext context)
        {
            _context = context;
        }

        // ------------------------------------------------------------------ small helpers

        private static string Iso(DateTime date) => date.ToString(LetterDateFormat, CultureInfo.InvariantCulture);
        private static string DayText(DateTime date) => date.ToString(DayTextFormat, CultureInfo.InvariantCulture);
        private static string Weekday(DateTime date) => date.ToString("dddd", CultureInfo.InvariantCulture);
        private static string Tidy(string? value) => string.Join(' ', (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        private static string? OrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static string WhoIs(StaffMaster s) => StaffService.Letter(s.Title, s.FirstName, s.MiddleName, s.LastName);

        private static bool TryParseDate(string? text, out DateTime date) =>
            DateTime.TryParseExact((text ?? "").Trim(), LetterDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        private static string Sentence(List<string> errors) => StaffLetterService.Sentence(errors.Distinct().ToList());

        private static string JoinNames(IEnumerable<string> names)
        {
            var list = names.Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return list.Count <= 5 ? string.Join(", ", list) : string.Join(", ", list.Take(5)) + $" and {list.Count - 5} more";
        }

        private static string? CanonicalHalf(string? half)
        {
            var text = Tidy(half);
            if (text == "") return "";
            if (text.Equals("First Half", StringComparison.OrdinalIgnoreCase) || text.Equals("First", StringComparison.OrdinalIgnoreCase)) return "First Half";
            if (text.Equals("Second Half", StringComparison.OrdinalIgnoreCase) || text.Equals("Second", StringComparison.OrdinalIgnoreCase)) return "Second Half";
            return null;
        }

        private async Task<DutyChart> LoadChartAsync(Guid id, bool withDays = false)
        {
            var query = _context.DutyCharts.AsQueryable();
            if (withDays) query = query.Include(c => c.Days);
            return await query.FirstOrDefaultAsync(c => c.Id == id) ?? throw new KeyNotFoundException("Duty chart not found.");
        }

        // ------------------------------------------------------------------ timetables

        private sealed record TimetableDay(DateTime Date, string? Morning, string? Afternoon, int Papers);

        /// <summary>
        /// The exam days of the college's regular (not revaluation) exams in an academic year, from the timetables: rows
        /// that start before 12:00 are the morning session, the others the afternoon, each shown as the most common time
        /// text of that session on the day.
        /// </summary>
        private async Task<List<TimetableDay>> TimetableDaysAsync(Guid ayid, DateTime from, DateTime to)
        {
            var examIds = await _context.Exams.AsNoTracking()
                .Where(e => e.AcademicYearAYID == ayid && e.RevaluationForExamId == null).Select(e => e.ExamId).ToListAsync();
            var rows = await _context.TimeTables.AsNoTracking()
                .Where(t => t.ExamId != null && examIds.Contains(t.ExamId.Value) && t.Date != null && t.Date != "" && t.Time != null && t.Time != "")
                .Select(t => new { Date = t.Date!, Time = t.Time!, t.SubjectId, t.CourseId })
                .ToListAsync();

            var days = new List<TimetableDay>();
            foreach (var group in rows.Select(r => (Date: ExamSlotOrder.Date(r.Date), Time: r.Time.Trim(), r.SubjectId, r.CourseId))
                         .Where(r => r.Date != DateTime.MaxValue && r.Date.Date >= from.Date && r.Date.Date <= to.Date)
                         .GroupBy(r => r.Date.Date).OrderBy(g => g.Key))
            {
                var morning = group.Where(r => ExamSlotOrder.StartTime(r.Time) < TimeSpan.FromHours(12)).Select(r => r.Time).ToList();
                var afternoon = group.Where(r => ExamSlotOrder.StartTime(r.Time) >= TimeSpan.FromHours(12)).Select(r => r.Time).ToList();
                var papers = group.Select(r => r.SubjectId?.ToString() ?? "course " + r.CourseId).Distinct().Count();
                days.Add(new TimetableDay(group.Key, MostCommon(morning), MostCommon(afternoon), papers));
            }
            return days;
        }

        private static string? MostCommon(List<string> times) =>
            times.Count == 0 ? null
                : times.GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count()).ThenBy(g => ExamSlotOrder.StartTime(g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .First().Key;

        /// <summary>Adds the timetable days the chart does not have; with <paramref name="fillEmpty"/> also fills empty session times of days that came from a timetable. A hand-edited day is never changed.</summary>
        private async Task AddTimetableDaysAsync(DutyChart chart, bool fillEmpty)
        {
            foreach (var t in await TimetableDaysAsync(chart.AYID, chart.FromDate, chart.ToDate))
            {
                var day = chart.Days.FirstOrDefault(d => d.Date.Date == t.Date);
                if (day == null)
                {
                    _context.DutyChartDays.Add(new DutyChartDay
                    {
                        Id = Guid.NewGuid(), CollegeId = chart.CollegeId, ChartId = chart.Id, Date = t.Date,
                        Morning = t.Morning, Afternoon = t.Afternoon, FromTimetable = true
                    });
                }
                else if (fillEmpty && day.FromTimetable)
                {
                    day.Morning ??= t.Morning;
                    day.Afternoon ??= t.Afternoon;
                }
            }
        }

        // ------------------------------------------------------------------ list and detail

        private static DutyChartSummaryDto Summary(DutyChart c, int dayCount, int memberCount, IEnumerable<(string Kind, string? Session)> entries, int letterCount)
        {
            var list = entries.ToList();
            return new DutyChartSummaryDto
            {
                Id = c.Id,
                Name = c.Name,
                Half = c.Half,
                FromDate = Iso(c.FromDate),
                ToDate = Iso(c.ToDate),
                Note = c.Note,
                DayCount = dayCount,
                StaffCount = memberCount,
                DutyCount = list.Where(e => e.Kind == DutyKinds.Duty).Sum(e => e.Session == DutySessions.Both ? 2 : 1),
                LetterCount = letterCount
            };
        }

        public async Task<List<DutyChartSummaryDto>> ListAsync(Guid ayid)
        {
            var charts = await _context.DutyCharts.AsNoTracking().Where(c => ayid == Guid.Empty || c.AYID == ayid).ToListAsync();
            var ids = charts.Select(c => c.Id).ToList();
            var dayCounts = (await _context.DutyChartDays.AsNoTracking().Where(d => ids.Contains(d.ChartId))
                .GroupBy(d => d.ChartId).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync()).ToDictionary(g => g.Id, g => g.Count);
            var memberCounts = (await _context.DutyChartMembers.AsNoTracking().Where(m => ids.Contains(m.ChartId))
                .GroupBy(m => m.ChartId).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync()).ToDictionary(g => g.Id, g => g.Count);
            var entries = (await _context.DutyChartEntries.AsNoTracking().Where(e => ids.Contains(e.ChartId))
                .Select(e => new { e.ChartId, e.Kind, e.Session }).ToListAsync()).ToLookup(e => e.ChartId);
            var letterCounts = (await _context.StaffLetters.AsNoTracking()
                .Where(l => l.DutyChartId != null && ids.Contains(l.DutyChartId.Value) && l.LetterType == StaffLetterTypes.Supervision)
                .GroupBy(l => l.DutyChartId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync()).ToDictionary(g => g.Id, g => g.Count);

            return charts.OrderByDescending(c => c.FromDate).ThenByDescending(c => c.CreatedAt)
                .Select(c => Summary(c, dayCounts.GetValueOrDefault(c.Id), memberCounts.GetValueOrDefault(c.Id), entries[c.Id].Select(e => (e.Kind, e.Session)), letterCounts.GetValueOrDefault(c.Id)))
                .ToList();
        }

        public async Task<DutyChartDetailDto> GetAsync(Guid id)
        {
            var chart = await _context.DutyCharts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id) ?? throw new KeyNotFoundException("Duty chart not found.");
            var days = await _context.DutyChartDays.AsNoTracking().Where(d => d.ChartId == id).OrderBy(d => d.Date).ToListAsync();
            var members = await _context.DutyChartMembers.AsNoTracking().Where(m => m.ChartId == id).OrderBy(m => m.SortOrder).ThenBy(m => m.StaffId).ToListAsync();
            var entries = await _context.DutyChartEntries.AsNoTracking().Where(e => e.ChartId == id).OrderBy(e => e.Date).ThenBy(e => e.StaffId).ToListAsync();
            var letters = await _context.StaffLetters.AsNoTracking().Include(l => l.Duties)
                .Where(l => l.DutyChartId == id && l.LetterType == StaffLetterTypes.Supervision).ToListAsync();
            var papers = (await TimetableDaysAsync(chart.AYID, chart.FromDate, chart.ToDate)).ToDictionary(t => t.Date, t => t.Papers);

            return new DutyChartDetailDto
            {
                Chart = Summary(chart, days.Count, members.Count, entries.Select(e => (e.Kind, e.Session)), letters.Count),
                Days = days.Select(d => new DutyChartDayDto
                {
                    Date = Iso(d.Date), Weekday = Weekday(d.Date), Morning = d.Morning, Afternoon = d.Afternoon,
                    FromTimetable = d.FromTimetable, Papers = papers.GetValueOrDefault(d.Date.Date)
                }).ToList(),
                Members = members.Select(m => new DutyChartMemberDto { StaffId = m.StaffId, SortOrder = m.SortOrder }).ToList(),
                Entries = entries.Select(e => new DutyChartEntryDto
                {
                    StaffId = e.StaffId, Date = Iso(e.Date), Kind = e.Kind, Session = e.Session, Role = e.Role, Note = e.Note
                }).ToList(),
                Roles = SupervisionRoles.All.ToList(),
                Letters = letters.OrderBy(l => l.SerialNo).Select(l => new DutyChartLetterDto
                {
                    LetterId = l.Id,
                    StaffId = l.StaffId,
                    StaffName = l.StaffName,
                    LetterNo = l.LetterNo,
                    LetterDate = Iso(l.LetterDate),
                    Role = l.Role ?? "",
                    Lines = l.Duties.OrderBy(d => d.SortOrder).Select(d => new DutyLineDto
                    {
                        Date = LineDate(d.Date), Time = d.Time, Kind = d.Kind, Role = d.Role ?? ""
                    }).ToList()
                }).ToList()
            };
        }

        /// <summary>A letter line's date (saved as dd-MM-yyyy text) as yyyy-MM-dd; unreadable text stays as it is.</summary>
        private static string LineDate(string text)
        {
            var date = ExamSlotOrder.Date(text);
            return date == DateTime.MaxValue ? text : Iso(date);
        }

        // ------------------------------------------------------------------ the chart itself

        private sealed record ChartFields(string Name, string Half, DateTime From, DateTime To, string? Note);

        private static ChartFields CheckFields(DutyChartSave dto)
        {
            var errors = new List<string>();
            var name = Tidy(dto.Name);
            if (name == "") errors.Add("enter a name for the chart.");
            else if (name.Length > 200) errors.Add("the name is longer than 200 characters.");
            var half = CanonicalHalf(dto.Half);
            if (half == null) errors.Add("the half must be First Half, Second Half or empty.");
            var note = OrNull(dto.Note);
            if (note != null && note.Length > 500) errors.Add("the note is longer than 500 characters.");

            DateTime from = default, to = default;
            var fromOk = TryParseDate(dto.FromDate, out from);
            var toOk = TryParseDate(dto.ToDate, out to);
            if (!fromOk) errors.Add("the first date is not a valid date.");
            if (!toOk) errors.Add("the last date is not a valid date.");
            if (fromOk && toOk)
            {
                if (to < from) errors.Add("the last date is before the first date.");
                else if ((to - from).TotalDays >= MaxDays) errors.Add("a chart can cover at most a year.");
            }
            if (errors.Count > 0) throw new InvalidOperationException(Sentence(errors));
            return new ChartFields(name, half!, from.Date, to.Date, note);
        }

        public async Task<DutyChartDetailDto> CreateAsync(DutyChartSave dto)
        {
            if (_context.CurrentCollegeId == null)
                throw new InvalidOperationException("Duty charts belong to a college: sign in as that college's staff.");
            var fields = CheckFields(dto);
            if (!await _context.AcademicYears.AnyAsync(a => a.AYID == dto.Ayid))
                throw new InvalidOperationException("Choose the academic year.");

            var chart = new DutyChart
            {
                Id = Guid.NewGuid(), CollegeId = _context.CurrentCollegeId, AYID = dto.Ayid, Name = fields.Name, Half = fields.Half,
                FromDate = fields.From, ToDate = fields.To, Note = fields.Note
            };
            _context.DutyCharts.Add(chart);
            await AddTimetableDaysAsync(chart, fillEmpty: false);
            await _context.SaveChangesAsync();
            return await GetAsync(chart.Id);
        }

        public async Task<DutyChartDetailDto> UpdateAsync(Guid id, DutyChartSave dto)
        {
            var chart = await LoadChartAsync(id, withDays: true);
            var fields = CheckFields(dto);
            if (dto.Ayid != Guid.Empty && dto.Ayid != chart.AYID)
                throw new InvalidOperationException("The academic year of a chart cannot be changed.");

            // Shrinking the dates drops the days outside them, unless someone has something on one of those days.
            var outside = chart.Days.Where(d => d.Date.Date < fields.From || d.Date.Date > fields.To).ToList();
            if (outside.Count > 0)
            {
                var dates = outside.Select(d => d.Date.Date).ToHashSet();
                var busy = await EntriesOnAsync(chart.Id, d => dates.Contains(d));
                if (busy.Count > 0)
                    throw new InvalidOperationException($"{JoinNames(busy.Select(b => b.Who))} {(busy.Select(b => b.Who).Distinct().Count() == 1 ? "has" : "have")} entries on days outside the new dates ({string.Join(", ", busy.Select(b => DayText(b.Date)).Distinct())}). Clear them first.");
                _context.DutyChartDays.RemoveRange(outside);
                foreach (var day in outside) chart.Days.Remove(day);
            }

            chart.Name = fields.Name;
            chart.Half = fields.Half;
            chart.FromDate = fields.From;
            chart.ToDate = fields.To;
            chart.Note = fields.Note;
            await AddTimetableDaysAsync(chart, fillEmpty: false);
            await _context.SaveChangesAsync();
            return await GetAsync(chart.Id);
        }

        public async Task<int> DeleteAsync(Guid id)
        {
            var chart = await LoadChartAsync(id);
            var letters = await _context.StaffLetters.Where(l => l.DutyChartId == id).ToListAsync();
            var now = DateTime.UtcNow;
            foreach (var letter in letters)
            {
                letter.IsDeleted = true;
                letter.DeletedAt = now;
            }
            chart.IsDeleted = true;
            chart.DeletedAt = now;
            await _context.SaveChangesAsync();
            return letters.Count;
        }

        /// <summary>The people with an entry on the dates matching the test, with the date of each entry.</summary>
        private async Task<List<(string Who, DateTime Date)>> EntriesOnAsync(Guid chartId, Func<DateTime, bool> onDate, Func<DutyChartEntry, bool>? entryTest = null)
        {
            var entries = (await _context.DutyChartEntries.AsNoTracking().Where(e => e.ChartId == chartId).ToListAsync())
                .Where(e => onDate(e.Date.Date) && (entryTest == null || entryTest(e))).ToList();
            var ids = entries.Select(e => e.StaffId).Distinct().ToList();
            var names = (await _context.StaffMasters.AsNoTracking().Where(s => ids.Contains(s.StaffId)).ToListAsync()).ToDictionary(s => s.StaffId, WhoIs);
            return entries.Select(e => (names.GetValueOrDefault(e.StaffId, "Someone"), e.Date.Date)).ToList();
        }

        // ------------------------------------------------------------------ days

        public async Task<DutyChartDetailDto> RefreshDaysAsync(Guid id)
        {
            var chart = await LoadChartAsync(id, withDays: true);
            await AddTimetableDaysAsync(chart, fillEmpty: true);
            await _context.SaveChangesAsync();
            return await GetAsync(id);
        }

        public async Task<DutyChartDetailDto> SaveDaysAsync(Guid id, List<DutyChartDaySave> days)
        {
            var chart = await LoadChartAsync(id, withDays: true);
            var errors = new List<string>();
            var wanted = new Dictionary<DateTime, (string? Morning, string? Afternoon)>();
            foreach (var d in days ?? new List<DutyChartDaySave>())
            {
                if (!TryParseDate(d.Date, out var date))
                {
                    errors.Add($"\"{(d.Date ?? "").Trim()}\" is not a valid date.");
                    continue;
                }
                var morning = OrNull(d.Morning);
                var afternoon = OrNull(d.Afternoon);
                if (date < chart.FromDate.Date || date > chart.ToDate.Date) errors.Add($"{DayText(date)} is outside the dates of the chart; change the dates first.");
                else if (wanted.ContainsKey(date)) errors.Add($"{DayText(date)} is listed more than once.");
                else if (morning == null && afternoon == null) errors.Add($"{DayText(date)} needs a morning or an afternoon time.");
                else if ((morning?.Length ?? 0) > 50 || (afternoon?.Length ?? 0) > 50) errors.Add($"The times on {DayText(date)} are longer than 50 characters.");
                else wanted[date] = (morning, afternoon);
            }

            // A day, or a session of a day, that someone has an entry on cannot go.
            var removed = chart.Days.Where(d => !wanted.ContainsKey(d.Date.Date)).Select(d => d.Date.Date).ToHashSet();
            foreach (var group in (await EntriesOnAsync(chart.Id, d => removed.Contains(d))).GroupBy(e => e.Date).OrderBy(g => g.Key))
                errors.Add($"{JoinNames(group.Select(e => e.Who))} {(group.Select(e => e.Who).Distinct().Count() == 1 ? "has" : "have")} an entry on {DayText(group.Key)}, so that day cannot be removed.");
            foreach (var day in chart.Days.Where(d => wanted.ContainsKey(d.Date.Date)))
            {
                var (morning, afternoon) = wanted[day.Date.Date];
                foreach (var (session, label, gone) in new[] { ("M", "morning", morning == null), ("A", "afternoon", afternoon == null) })
                {
                    if (!gone || (session == "M" ? day.Morning : day.Afternoon) == null) continue;
                    var users = await EntriesOnAsync(chart.Id, d => d == day.Date.Date, e => e.Kind == DutyKinds.Duty && (e.Session == DutySessions.Both || e.Session == session));
                    if (users.Count > 0)
                        errors.Add($"{JoinNames(users.Select(u => u.Who))} {(users.Select(u => u.Who).Distinct().Count() == 1 ? "is" : "are")} on duty in the {label} of {DayText(day.Date)}, so that session cannot be removed.");
                }
            }
            if (errors.Count > 0) throw new InvalidOperationException(Sentence(errors));

            foreach (var day in chart.Days.ToList())
            {
                if (!wanted.TryGetValue(day.Date.Date, out var times))
                {
                    _context.DutyChartDays.Remove(day);
                    chart.Days.Remove(day);
                    continue;
                }
                if (day.Morning != times.Morning || day.Afternoon != times.Afternoon)
                {
                    day.Morning = times.Morning;
                    day.Afternoon = times.Afternoon;
                    day.FromTimetable = false;
                }
            }
            foreach (var (date, times) in wanted.Where(w => chart.Days.All(d => d.Date.Date != w.Key)))
            {
                _context.DutyChartDays.Add(new DutyChartDay
                {
                    Id = Guid.NewGuid(), CollegeId = chart.CollegeId, ChartId = chart.Id, Date = date,
                    Morning = times.Morning, Afternoon = times.Afternoon, FromTimetable = false
                });
            }
            await _context.SaveChangesAsync();
            return await GetAsync(id);
        }

        // ------------------------------------------------------------------ entries and letters

        /// <summary>One checked entry, ready to save.</summary>
        private sealed record Planned(Guid StaffId, DateTime Date, string Kind, string? Session, string? Role, string? Note);

        private static string? CanonicalSession(string? session)
        {
            var text = new string((session ?? "").Where(char.IsLetter).ToArray());
            return DutySessions.All.FirstOrDefault(s => string.Equals(s, text, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<DutyChartSaveResult> SaveEntriesAsync(Guid id, DutyChartEntriesSave dto)
        {
            var chart = await LoadChartAsync(id, withDays: true);
            var days = chart.Days.ToDictionary(d => d.Date.Date);
            var requested = dto.Entries ?? new List<DutyChartEntryDto>();
            var listed = dto.Members?.Distinct().ToList();
            var staffIds = requested.Select(e => e.StaffId).Concat(listed ?? new List<Guid>()).Distinct().ToList();
            var staff = await _context.StaffMasters.AsNoTracking().Where(s => staffIds.Contains(s.StaffId)).ToDictionaryAsync(s => s.StaffId);

            var errors = new List<string>();
            // People who are only on the list (no entries) are checked like the people with entries.
            var withEntries = requested.Select(e => e.StaffId).ToHashSet();
            foreach (var memberId in (listed ?? new List<Guid>()).Where(m => !withEntries.Contains(m)))
            {
                if (!staff.TryGetValue(memberId, out var member)) errors.Add("a member of staff in the list was not found.");
                else if (!member.IsActive) errors.Add($"{WhoIs(member)} has left and cannot be on the chart.");
                else if (member.IsExternal) errors.Add($"{WhoIs(member)} is an external member of staff; charts are for our own staff.");
            }
            var plan = new List<Planned>();
            var seen = new HashSet<(Guid, DateTime)>();
            foreach (var e in requested)
            {
                if (!staff.TryGetValue(e.StaffId, out var person))
                {
                    errors.Add("a member of staff in the list was not found.");
                    continue;
                }
                var who = WhoIs(person);
                if (!TryParseDate(e.Date, out var date))
                {
                    errors.Add($"{who} has an entry with a date that is not valid (\"{(e.Date ?? "").Trim()}\").");
                    continue;
                }
                var on = DayText(date);

                var problems = new List<string>();
                if (!person.IsActive) problems.Add($"{who} has left and cannot be given duties.");
                else if (person.IsExternal) problems.Add($"{who} is an external member of staff; duties are for our own staff.");
                if (!days.TryGetValue(date, out var day)) problems.Add($"{who} has an entry on {on}, which is not a day of this chart.");
                if (!seen.Add((e.StaffId, date))) problems.Add($"{who} appears more than once on {on}.");

                var kind = DutyKinds.All.FirstOrDefault(k => string.Equals(k, (e.Kind ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                string? session = null, role = null, note = null;
                if (kind == null) problems.Add($"{who} has an entry on {on} that is not a duty, valuation or other work.");
                else if (kind == DutyKinds.Duty)
                {
                    session = CanonicalSession(e.Session);
                    if (session == null) problems.Add($"{who} needs a session (M, A or MA) on {on}.");
                    else if (day != null)
                    {
                        if (session != DutySessions.Afternoon && day.Morning == null) problems.Add($"{who} is given the morning of {on}, but there is no morning session that day.");
                        if (session != DutySessions.Morning && day.Afternoon == null) problems.Add($"{who} is given the afternoon of {on}, but there is no afternoon session that day.");
                    }
                    var typed = Tidy(e.Role);
                    role = typed == "" ? SupervisionRoles.DefaultFor(person) : SupervisionRoles.All.FirstOrDefault(r => string.Equals(r, typed, StringComparison.OrdinalIgnoreCase));
                    if (role == null) problems.Add($"{who} has the role \"{typed}\" on {on}; the roles are {string.Join(", ", SupervisionRoles.All)}.");
                }
                else if (kind == DutyKinds.Other)
                {
                    note = OrNull(Tidy(e.Note));
                    if (note != null && note.Length > 100) problems.Add($"The note for {who} on {on} is longer than 100 characters.");
                }

                errors.AddRange(problems);
                if (problems.Count == 0) plan.Add(new Planned(e.StaffId, date, kind!, session, role, note));
            }

            DateTime? letterDate = null;
            if (!string.IsNullOrWhiteSpace(dto.LetterDate))
            {
                if (TryParseDate(dto.LetterDate, out var parsed)) letterDate = parsed.Date;
                else errors.Add("the letter date is not a valid date.");
            }
            if (errors.Count > 0) throw new InvalidOperationException(Sentence(errors));

            // The members in order; people with entries who are not listed are added at the end. Without a list, the
            // people with entries stay in the order they already had.
            List<Guid> members;
            if (listed != null) members = listed.ToList();
            else
            {
                var current = await _context.DutyChartMembers.AsNoTracking().Where(m => m.ChartId == chart.Id).OrderBy(m => m.SortOrder).Select(m => m.StaffId).ToListAsync();
                members = current.Where(withEntries.Contains).ToList();
            }
            members.AddRange(requested.Select(e => e.StaffId).Distinct().Where(s => !members.Contains(s)));
            return await ApplyAsync(chart, plan, members, letterDate);
        }

        /// <summary>One line of a supervision letter.</summary>
        private sealed record Line(string Date, string Time, string Kind, string Role);

        /// <summary>
        /// Makes the chart's entries and supervision letters match the plan. A person with at least one duty has one letter
        /// (a new one, or the existing one with its number and date kept); letters of people without a duty are cancelled.
        /// Nothing is saved unless the letter date is there when a new letter needs it.
        /// </summary>
        private async Task<DutyChartSaveResult> ApplyAsync(DutyChart chart, List<Planned> plan, List<Guid> members, DateTime? letterDate)
        {
            var days = chart.Days.ToDictionary(d => d.Date.Date);
            var existingEntries = await _context.DutyChartEntries.Where(e => e.ChartId == chart.Id).ToListAsync();
            var existingLetters = (await _context.StaffLetters.Include(l => l.Duties)
                .Where(l => l.DutyChartId == chart.Id && l.LetterType == StaffLetterTypes.Supervision).ToListAsync())
                .OrderBy(l => l.SerialNo).ToList();
            var staffIds = plan.Select(p => p.StaffId).Distinct().ToList();
            var staff = await _context.StaffMasters.AsNoTracking().Where(s => staffIds.Contains(s.StaffId)).ToDictionaryAsync(s => s.StaffId);

            // The letters the plan asks for.
            var wanted = new List<(StaffMaster Staff, string Role, List<Line> Lines)>();
            foreach (var person in plan.GroupBy(p => p.StaffId))
            {
                var duties = person.Where(p => p.Kind == DutyKinds.Duty).ToList();
                if (duties.Count == 0) continue;
                var lines = new List<Line>();
                foreach (var p in person.OrderBy(p => p.Date))
                {
                    var date = DayText(p.Date);
                    if (p.Kind == DutyKinds.Valuation) lines.Add(new Line(date, FullDay, DutyKinds.Valuation, ValuationRole));
                    else if (p.Kind == DutyKinds.Other) lines.Add(new Line(date, FullDay, DutyKinds.Other, Shorten(p.Note ?? OtherRole, 40)));
                    else
                    {
                        if (p.Session != DutySessions.Afternoon) lines.Add(new Line(date, days[p.Date].Morning!, DutyKinds.Duty, p.Role!));
                        if (p.Session != DutySessions.Morning) lines.Add(new Line(date, days[p.Date].Afternoon!, DutyKinds.Duty, p.Role!));
                    }
                }
                var role = duties.Select(d => d.Role!).OrderBy(r => SupervisionRoles.All.ToList().IndexOf(r)).First();
                wanted.Add((staff[person.Key], role, lines));
            }
            wanted = wanted.OrderBy(w => w.Staff.FirstName, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Staff.LastName, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Staff.StaffId).ToList();

            var byStaff = existingLetters.GroupBy(l => l.StaffId).ToDictionary(g => g.Key, g => g.First());
            if (letterDate == null && wanted.Any(w => !byStaff.ContainsKey(w.Staff.StaffId)))
                throw new InvalidOperationException("Enter the letter date.");

            // Members: keep the rows, set the order, add the new, remove the gone (their entries go with them: the plan only has members).
            var existingMembers = await _context.DutyChartMembers.Where(m => m.ChartId == chart.Id).ToListAsync();
            var memberRows = existingMembers.GroupBy(m => m.StaffId).ToDictionary(g => g.Key, g => g.First());
            for (int i = 0; i < members.Count; i++)
            {
                if (memberRows.TryGetValue(members[i], out var row)) row.SortOrder = i;
                else _context.DutyChartMembers.Add(new DutyChartMember { Id = Guid.NewGuid(), CollegeId = chart.CollegeId, ChartId = chart.Id, StaffId = members[i], SortOrder = i });
            }
            _context.DutyChartMembers.RemoveRange(existingMembers.Where(m => !members.Contains(m.StaffId) || memberRows[m.StaffId] != m));

            // Entries: update in place, add, remove.
            var byKey = existingEntries.ToDictionary(e => (e.StaffId, e.Date.Date));
            foreach (var p in plan)
            {
                if (byKey.TryGetValue((p.StaffId, p.Date), out var entry))
                {
                    entry.Kind = p.Kind;
                    entry.Session = p.Session;
                    entry.Role = p.Role;
                    entry.Note = p.Note;
                }
                else
                {
                    _context.DutyChartEntries.Add(new DutyChartEntry
                    {
                        Id = Guid.NewGuid(), CollegeId = chart.CollegeId, ChartId = chart.Id, StaffId = p.StaffId, Date = p.Date,
                        Kind = p.Kind, Session = p.Session, Role = p.Role, Note = p.Note
                    });
                }
            }
            var kept = plan.Select(p => (p.StaffId, p.Date)).ToHashSet();
            _context.DutyChartEntries.RemoveRange(existingEntries.Where(e => !kept.Contains((e.StaffId, e.Date.Date))));

            // Letters.
            var result = new DutyChartSaveResult();
            var collegeId = chart.CollegeId ?? _context.CurrentCollegeId;
            LetterNumbering? numbering = null;
            foreach (var (person, role, lines) in wanted)
            {
                if (byStaff.TryGetValue(person.StaffId, out var letter))
                {
                    var same = letter.Role == role && letter.ExamTitle == chart.Name && letter.Half == chart.Half
                        && letter.Duties.OrderBy(d => d.SortOrder).Select(d => new Line(d.Date, d.Time, d.Kind, d.Role ?? "")).SequenceEqual(lines);
                    if (same)
                    {
                        result.Unchanged++;
                        continue;
                    }
                    letter.Role = role;
                    letter.ExamTitle = chart.Name;
                    letter.Half = chart.Half;
                    letter.UpdatedAt = DateTime.UtcNow;
                    _context.StaffLetterDuties.RemoveRange(letter.Duties.ToList());
                    AddLines(letter, lines, collegeId);
                    result.Updated++;
                    continue;
                }

                numbering ??= await LetterNumbering.OpenAsync(_context, collegeId, chart.AYID, StaffLetterTypes.Supervision);
                var (serial, number) = numbering.Take();
                letter = new StaffLetter
                {
                    Id = Guid.NewGuid(),
                    CollegeId = collegeId,
                    StaffId = person.StaffId,
                    LetterType = StaffLetterTypes.Supervision,
                    DutyChartId = chart.Id,
                    AYID = chart.AYID,
                    SerialNo = serial,
                    LetterNo = number,
                    LetterDate = letterDate!.Value,
                    Role = role,
                    StaffName = WhoIs(person),
                    ExamTitle = chart.Name,
                    Half = chart.Half
                };
                AddLines(letter, lines, collegeId);
                _context.StaffLetters.Add(letter);
                result.Created++;
            }

            var keptStaff = wanted.Select(w => w.Staff.StaffId).ToHashSet();
            foreach (var letter in existingLetters.Where(l => !keptStaff.Contains(l.StaffId) || byStaff[l.StaffId] != l))
            {
                letter.IsDeleted = true;
                letter.DeletedAt = DateTime.UtcNow;
                result.Removed++;
            }

            await _context.SaveChangesAsync();
            result.Message = $"Saved: {result.Created} new, {result.Updated} updated, {result.Unchanged} unchanged, {result.Removed} cancelled.";
            return result;
        }

        private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

        private void AddLines(StaffLetter letter, List<Line> lines, Guid? collegeId)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                _context.StaffLetterDuties.Add(new StaffLetterDuty
                {
                    Id = Guid.NewGuid(), CollegeId = collegeId, LetterId = letter.Id, Date = lines[i].Date, Time = lines[i].Time,
                    Kind = lines[i].Kind, Role = lines[i].Role, SortOrder = i
                });
            }
        }

        // ------------------------------------------------------------------ Excel

        private const string LegendLabel = "Legend";
        /// <summary>Empty numbered lines of the sheet of a chart with nobody on it.</summary>
        private const int BlankRows = 30;

        /// <summary>
        /// The chart as a sheet to fill in: the people on the chart (in chart order inside each group), the exam days
        /// across, the codes of the current entries in the cells. A chart with nobody on it gives a blank template: the
        /// headings and empty numbered rows to type the people into.
        /// </summary>
        public async Task<(byte[] Bytes, string FileName)> ExcelAsync(Guid id)
        {
            var chart = await _context.DutyCharts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id) ?? throw new KeyNotFoundException("Duty chart not found.");
            var days = await _context.DutyChartDays.AsNoTracking().Where(d => d.ChartId == id).OrderBy(d => d.Date).ToListAsync();
            var entries = await _context.DutyChartEntries.AsNoTracking().Where(e => e.ChartId == id).ToListAsync();
            var memberIds = await _context.DutyChartMembers.AsNoTracking().Where(m => m.ChartId == id).OrderBy(m => m.SortOrder).ThenBy(m => m.StaffId).Select(m => m.StaffId).ToListAsync();
            var found = (await _context.StaffMasters.AsNoTracking().Where(s => memberIds.Contains(s.StaffId) && s.IsActive && !s.IsExternal).ToListAsync()).ToDictionary(s => s.StaffId);
            var staff = memberIds.Where(found.ContainsKey).Select(i => found[i]).ToList();   // chart order
            var cells = entries.ToDictionary(e => (e.StaffId, e.Date.Date));
            var leaders = entries.Where(e => e.Kind == DutyKinds.Duty && (e.Role == SupervisionRoles.ChiefConductor || e.Role == SupervisionRoles.Senior))
                .Select(e => e.StaffId).ToHashSet();

            var groups = new (string Label, List<StaffMaster> People)[]
            {
                ("Chief Conductor / Senior Supervisors", staff.Where(s => leaders.Contains(s.StaffId)).ToList()),
                ("Teaching staff", staff.Where(s => !leaders.Contains(s.StaffId) && s.IsTeaching).ToList()),
                ("Non-teaching staff", staff.Where(s => !leaders.Contains(s.StaffId) && !s.IsTeaching).ToList())
            };

            using var workbook = ExcelStyles.NewWorkbook();
            var sheet = workbook.Worksheets.Add("Duty Chart");
            const int FirstDay = 4;
            var lastColumn = FirstDay + days.Count;   // the Duties column
            var fill = XLColor.FromHtml("#E8EEF9");

            sheet.Cell(1, 1).Value = $"Duty Chart — {chart.Name} ({chart.FromDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)} to {chart.ToDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)})";
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 13;
            sheet.Cell(2, 1).Value = chart.Note ?? "";
            sheet.Cell(2, 1).Style.Font.Italic = true;

            var headers = new[] { "Sr. No.", "Employee ID", "Employee Name" };
            for (int i = 0; i < headers.Length; i++) sheet.Cell(3, i + 1).Value = headers[i];
            for (int i = 0; i < days.Count; i++)
                sheet.Cell(3, FirstDay + i).Value = days[i].Date.ToString(DayTextFormat, CultureInfo.InvariantCulture) + "\n" + days[i].Date.ToString("ddd", CultureInfo.InvariantCulture);
            sheet.Cell(3, lastColumn).Value = "Duties";
            var header = sheet.Range(3, 1, 3, lastColumn);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = fill;
            header.Style.Alignment.WrapText = true;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var row = 4;
            var serial = 1;
            foreach (var (label, people) in groups)
            {
                if (people.Count == 0) continue;
                sheet.Cell(row, 1).Value = label;
                var groupRow = sheet.Range(row, 1, row, lastColumn);
                groupRow.Style.Font.Bold = true;
                groupRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
                row++;
                foreach (var person in people)
                {
                    sheet.Cell(row, 1).Value = serial++;
                    sheet.Cell(row, 2).Value = person.EmployeeCode ?? "";
                    sheet.Cell(row, 2).Style.NumberFormat.Format = "@";
                    sheet.Cell(row, 3).Value = WhoIs(person);
                    var sessions = 0;
                    for (int i = 0; i < days.Count; i++)
                    {
                        if (!cells.TryGetValue((person.StaffId, days[i].Date.Date), out var entry)) continue;
                        sheet.Cell(row, FirstDay + i).Value = DutyCodes.CellText(entry);
                        if (entry.Kind == DutyKinds.Duty) sessions += entry.Session == DutySessions.Both ? 2 : 1;
                    }
                    sheet.Cell(row, lastColumn).Value = sessions;
                    row++;
                }
            }
            if (staff.Count == 0)
            {
                // Nobody on the chart yet: empty numbered lines, so the sheet works as a blank template.
                for (int n = 1; n <= BlankRows; n++) sheet.Cell(row++, 1).Value = n;
                sheet.Range(4, 2, row - 1, 2).Style.NumberFormat.Format = "@";
            }
            var lastData = row - 1;
            sheet.Range(4, FirstDay, Math.Max(lastData, 4), lastColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Range(4, 1, Math.Max(lastData, 4), 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (lastData >= 3) sheet.Range(3, 1, lastData, lastColumn).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorder(XLBorderStyleValues.Thin);

            // Legend: what the codes mean and the session times of each day (the import stops reading at "Legend").
            row += 1;
            sheet.Cell(row, 1).Value = LegendLabel;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            row++;
            foreach (var line in new[]
            {
                "M = morning, A = afternoon, M/A = both sessions of the day.",
                "CC = Chief Conductor, SS = Senior Supervisor, written before the session, e.g. CC (M/A). Other duties are Junior Supervisor (teaching staff) or Non-Teaching Staff.",
                "V = valuation (CAP assessment) day. Any other text = other work that day (e.g. Exam cell).",
                "Type the Employee ID of each person on duty (the name is optional) and fill in the cells, then upload the sheet again. The people in the sheet are the people on the chart; people missing from it are removed and lose their duties."
            })
            {
                sheet.Cell(row++, 1).Value = line;
            }
            foreach (var (label, pick) in new (string, Func<DutyChartDay, string?>)[] { ("Morning", d => d.Morning), ("Afternoon", d => d.Afternoon) })
            {
                sheet.Cell(row, 3).Value = label;
                sheet.Cell(row, 3).Style.Font.Bold = true;
                for (int i = 0; i < days.Count; i++) sheet.Cell(row, FirstDay + i).Value = pick(days[i]) ?? "";
                var times = sheet.Range(row, FirstDay, row, lastColumn - 1);
                times.Style.Alignment.WrapText = true;
                times.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                times.Style.Font.FontSize = 8;
                row++;
            }

            ExcelStyles.SetWidth(sheet.Column(1), 8);
            ExcelStyles.SetWidth(sheet.Column(2), 12);
            ExcelStyles.SetWidth(sheet.Column(3), 30);
            for (int c = FirstDay; c < lastColumn; c++) ExcelStyles.SetWidth(sheet.Column(c), 11);
            ExcelStyles.SetWidth(sheet.Column(lastColumn), 8);
            sheet.SheetView.Freeze(3, 3);
            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.FitToPages(1, 0);
            sheet.PageSetup.SetRowsToRepeatAtTop(3, 3);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = string.Concat($"Duty Chart {chart.Name}".Split(Path.GetInvalidFileNameChars())) + ".xlsx";
            return (stream.ToArray(), fileName);
        }

        public async Task<StaffImportResult> ImportAsync(Guid id, DutyChartImportRequest request)
        {
            if (request.File == null || request.File.Length == 0)
                throw new InvalidOperationException("Choose the filled-in duty chart sheet (.xlsx).");
            DateTime letterDate = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(request.LetterDate))
            {
                if (!TryParseDate(request.LetterDate, out letterDate)) throw new InvalidOperationException("The letter date is not a valid date.");
            }

            var chart = await LoadChartAsync(id, withDays: true);
            using var stream = new MemoryStream();
            await request.File.CopyToAsync(stream);
            XLWorkbook workbook;
            try
            {
                stream.Position = 0;
                workbook = new XLWorkbook(stream);
            }
            catch
            {
                throw new InvalidOperationException("The file could not be read. Use the downloaded .xlsx duty chart.");
            }

            using (workbook)
            {
                var sheet = workbook.Worksheet(1);
                var (headerRow, idColumn) = FindHeader(sheet);
                if (headerRow == 0)
                    throw new InvalidOperationException("This is not a duty chart sheet (no \"Employee ID\" heading). Download it with the Excel button, fill it in and upload it again.");
                var nameColumn = 0;
                var lastColumn = sheet.Row(headerRow).LastCellUsed()?.Address.ColumnNumber ?? idColumn;
                var dateColumns = new List<(int Column, DateTime Date)>();
                var errors = new List<string>();
                for (int c = 1; c <= lastColumn; c++)
                {
                    var cell = sheet.Cell(headerRow, c);
                    if (HeaderKey(cell.GetString()) == "employeename") nameColumn = c;
                    var date = HeaderDate(cell);
                    if (date == null) continue;
                    if (dateColumns.Any(d => d.Date == date)) errors.Add($"The date {DayText(date.Value)} is in the heading twice.");
                    else dateColumns.Add((c, date.Value));
                }
                if (dateColumns.Count == 0 && errors.Count == 0)
                    errors.Add("No dates were found in the heading row. Each exam day needs its own column headed with the date.");

                var days = chart.Days.ToDictionary(d => d.Date.Date);
                var allStaff = await _context.StaffMasters.AsNoTracking().Where(s => s.EmployeeCode != null && s.EmployeeCode != "").ToListAsync();
                var byCode = new Dictionary<string, StaffMaster>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in allStaff.OrderByDescending(s => s.IsActive && !s.IsExternal)) byCode.TryAdd(s.EmployeeCode!.Trim(), s);
                var existing = (await _context.DutyChartEntries.AsNoTracking().Where(e => e.ChartId == id).ToListAsync()).ToLookup(e => e.StaffId);
                var wasMember = (await _context.DutyChartMembers.AsNoTracking().Where(m => m.ChartId == id).Select(m => m.StaffId).ToListAsync()).ToHashSet();
                wasMember.UnionWith(existing.Select(g => g.Key));

                var rows = new List<StaffImportRow>();
                var plan = new List<Planned>();
                var seenStaff = new HashSet<Guid>();
                var members = new List<Guid>();   // the people of the sheet, in sheet order
                var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
                for (int r = headerRow + 1; r <= lastRow; r++)
                {
                    if (sheet.Cell(r, 1).GetString().Trim().StartsWith(LegendLabel, StringComparison.OrdinalIgnoreCase)) break;
                    var code = sheet.Cell(r, idColumn).GetFormattedString().Trim();
                    var typedName = nameColumn > 0 ? Tidy(sheet.Cell(r, nameColumn).GetFormattedString()) : "";
                    var cellsInRow = dateColumns.Select(d => (d.Date, Text: Tidy(sheet.Cell(r, d.Column).GetFormattedString()))).Where(c => c.Text != "").ToList();

                    if (code == "")
                    {
                        if (cellsInRow.Count > 0) AddRowError(rows, errors, r, typedName, "has no Employee ID.");
                        continue;   // a group heading or an empty line
                    }
                    if (!byCode.TryGetValue(code, out var person))
                    {
                        AddRowError(rows, errors, r, typedName, $"Employee ID {code} is not in Staff Master.");
                        continue;
                    }
                    var who = WhoIs(person);
                    if (!person.IsActive) { AddRowError(rows, errors, r, who, $"{who} has left and cannot be given duties."); continue; }
                    if (person.IsExternal) { AddRowError(rows, errors, r, who, $"{who} is an external member of staff; duties are for our own staff."); continue; }
                    if (!seenStaff.Add(person.StaffId)) { AddRowError(rows, errors, r, who, $"Employee ID {code} is in the sheet more than once."); continue; }
                    members.Add(person.StaffId);

                    var rowErrors = new List<string>();
                    var mine = new List<Planned>();
                    foreach (var (date, text) in cellsInRow)
                    {
                        var on = DayText(date);
                        if (!days.TryGetValue(date, out var day))
                        {
                            rowErrors.Add($"{on} is not a day of this chart; add it under Days.");
                            continue;
                        }
                        var parsed = DutyCodes.Parse(text, out var error);
                        if (error != null)
                        {
                            rowErrors.Add($"{on}: {error}");
                            continue;
                        }
                        if (parsed == null) continue;
                        if (parsed.Kind == DutyKinds.Duty)
                        {
                            if (parsed.Session != DutySessions.Afternoon && day.Morning == null) { rowErrors.Add($"{on}: there is no morning session that day."); continue; }
                            if (parsed.Session != DutySessions.Morning && day.Afternoon == null) { rowErrors.Add($"{on}: there is no afternoon session that day."); continue; }
                        }
                        mine.Add(new Planned(person.StaffId, date, parsed.Kind, parsed.Session,
                            parsed.Kind == DutyKinds.Duty ? parsed.Role ?? SupervisionRoles.DefaultFor(person) : null, parsed.Note));
                    }

                    var before = existing[person.StaffId].ToList();

                    var notes = new List<string>();
                    if (typedName != "" && !SameName(typedName, person)) notes.Add($"the name in the sheet (\"{typedName}\") differs from Staff Master, which is used");
                    string action;
                    if (rowErrors.Count > 0) action = "";
                    else if (!wasMember.Contains(person.StaffId)) action = StaffImportActions.New;   // added to the chart
                    else if (mine.Count == 0 && before.Count == 0) action = StaffImportActions.Unchanged;
                    else if (mine.Count == before.Count && mine.All(m => before.Any(b => Same(b, m)))) action = StaffImportActions.Unchanged;
                    else
                    {
                        action = StaffImportActions.Update;
                        if (mine.Count == 0) notes.Add("all duties are removed");
                    }
                    rows.Add(new StaffImportRow
                    {
                        Row = r, Name = who, Email = "", Action = action, HasErrors = rowErrors.Count > 0,
                        Note = rowErrors.Count > 0 ? Sentence(rowErrors) : notes.Count > 0 ? Sentence(new List<string> { string.Join("; ", notes) + "." }) : null
                    });
                    if (rowErrors.Count > 0) errors.Add($"Row {r}: {Sentence(rowErrors)}");
                    plan.AddRange(mine);
                }

                var result = new StaffImportResult { Rows = rows };
                if (errors.Count > 0)
                {
                    result.Message = $"Nothing was imported. Fix {errors.Count} problem(s) and import again.";
                    result.Errors = errors;
                    return result;
                }

                var added = rows.Count(r => r.Action == StaffImportActions.New);
                var removed = wasMember.Count(s => !seenStaff.Contains(s));
                var counts = $"{added} new, {rows.Count(r => r.Action == StaffImportActions.Update)} updated, {rows.Count(r => r.Action == StaffImportActions.Unchanged)} unchanged";
                result.Success = true;
                if (request.Preview)
                {
                    result.Message = $"Ready: {counts}.{Membership(added, removed, past: false)} Check the list, then press Save.";
                    return result;
                }

                var saved = await ApplyAsync(chart, plan, members, letterDate.Date);
                result.Saved = true;
                result.Message = $"{saved.Message}{Membership(added, removed, past: true)}";
                return result;
            }
        }

        /// <summary>How many people the sheet adds to the chart and removes from it, as a sentence (empty when nobody).</summary>
        private static string Membership(int added, int removed, bool past)
        {
            static string People(int n) => n == 1 ? "1 person" : $"{n} people";
            static string Verb(int n, bool past) => past ? (n == 1 ? "was" : "were") : "will be";
            var text = "";
            if (added > 0) text += $" {People(added)} {Verb(added, past)} added to the chart.";
            if (removed > 0) text += $" {People(removed)} not in the sheet {Verb(removed, past)} removed from the chart, with any duties they had.";
            return text;
        }

        private static void AddRowError(List<StaffImportRow> rows, List<string> errors, int row, string name, string problem)
        {
            rows.Add(new StaffImportRow { Row = row, Name = name, Email = "", Action = "", HasErrors = true, Note = Sentence(new List<string> { problem }) });
            errors.Add($"Row {row}: {Sentence(new List<string> { problem })}");
        }

        private static bool Same(DutyChartEntry old, Planned now) =>
            old.Date.Date == now.Date && old.Kind == now.Kind && old.Session == now.Session && old.Role == now.Role && (old.Note ?? "") == (now.Note ?? "");

        /// <summary>The name typed in the sheet is the person's, ignoring title, case and punctuation.</summary>
        private static bool SameName(string typed, StaffMaster person)
        {
            static string Plain(string name) => Regex.Replace(Regex.Replace(name, @"^(prof|dr|mr|mrs|ms|miss)\b\.?\s*", "", RegexOptions.IgnoreCase), @"[^\p{L}\p{N}]", "").ToLowerInvariant();
            return Plain(typed) == Plain(WhoIs(person));
        }

        private static string HeaderKey(string text) => Regex.Replace(text ?? "", @"[\s._]", "").ToLowerInvariant();

        /// <summary>The row (in the first ten) and column of the "Employee ID" heading; row 0 when there is none.</summary>
        private static (int Row, int Column) FindHeader(IXLWorksheet sheet)
        {
            for (int r = 1; r <= 10; r++)
            {
                var last = sheet.Row(r).LastCellUsed()?.Address.ColumnNumber ?? 0;
                for (int c = 1; c <= last; c++)
                    if (HeaderKey(sheet.Cell(r, c).GetString()) == "employeeid") return (r, c);
            }
            return (0, 0);
        }

        private static readonly string[] HeaderDateFormats =
        {
            "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy",
            "dd MMM yyyy", "d MMM yyyy", "dd-MMM-yyyy", "d-MMM-yyyy", "dd MMMM yyyy", "d MMMM yyyy"
        };

        /// <summary>A heading cell's date: an Excel date, or text like "10-11-2025" or "10 Nov 2025" with or without a weekday.</summary>
        private static DateTime? HeaderDate(IXLCell cell)
        {
            if (cell.IsEmpty()) return null;
            if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime().Date;
            var text = Regex.Replace(cell.GetString(), @"\b(mon|tue|wed|thu|fri|sat|sun)[a-z]*\b|[,()]", " ", RegexOptions.IgnoreCase);
            text = Tidy(text);
            return DateTime.TryParseExact(text, HeaderDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.Date : null;
        }
    }

    /// <summary>The codes of the duty chart sheet: written for a cell, and read back tolerantly.</summary>
    public static class DutyCodes
    {
        /// <summary>What a cell of the sheet holds: "M", "A", "M/A" (prefixed "CC (...)" or "SS (...)" for those roles), "V", or the note of other work.</summary>
        public static string CellText(DutyChartEntry entry)
        {
            if (entry.Kind == DutyKinds.Valuation) return "V";
            if (entry.Kind == DutyKinds.Other) return string.IsNullOrWhiteSpace(entry.Note) ? "O" : entry.Note;
            var code = entry.Session == DutySessions.Both ? "M/A" : entry.Session == DutySessions.Afternoon ? "A" : "M";
            return entry.Role == SupervisionRoles.ChiefConductor ? $"CC ({code})" : entry.Role == SupervisionRoles.Senior ? $"SS ({code})" : code;
        }

        /// <summary>What a cell says; Role is null when the cell does not name one (the person's default applies).</summary>
        public sealed record Code(string Kind, string? Session, string? Role, string? Note);

        private static readonly string[] MorningWords = { "M", "AM", "MORNING" };
        private static readonly string[] AfternoonWords = { "A", "PM", "AFTERNOON" };
        private static readonly string[] BothWords = { "M/A", "MA", "A/M", "AM/PM", "M&A", "M+A", "BOTH" };
        private static readonly string[] ValuationWords = { "V", "VAL", "VALUATION", "CAP" };

        /// <summary>
        /// Reads a cell: M, A, M/A (also MA, AM = morning, PM = afternoon), with CC or SS before or after (brackets and
        /// spaces do not matter), V for valuation, anything else as other work. Null for an empty cell or a dash. When
        /// the cell cannot be read, <paramref name="error"/> says why.
        /// </summary>
        public static Code? Parse(string? text, out string? error)
        {
            error = null;
            var original = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (original == "") return null;
            var tokens = Regex.Replace(original.ToUpperInvariant(), @"[()\[\]{}\-–—·•,:;]", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count == 0) return null;

            string? role = null;
            string? RoleOf(string token) => token == "CC" ? SupervisionRoles.ChiefConductor : token == "SS" ? SupervisionRoles.Senior : null;
            if (RoleOf(tokens[0]) != null) { role = RoleOf(tokens[0]); tokens.RemoveAt(0); }
            else if (RoleOf(tokens[^1]) != null) { role = RoleOf(tokens[^1]); tokens.RemoveAt(tokens.Count - 1); }

            var joined = string.Concat(tokens);
            string? session = BothWords.Contains(joined) ? DutySessions.Both
                : MorningWords.Contains(joined) ? DutySessions.Morning
                : AfternoonWords.Contains(joined) ? DutySessions.Afternoon : null;
            if (session != null) return new Code(DutyKinds.Duty, session, role, null);

            if (role != null)
            {
                error = $"cannot read \"{original}\"; write the session as M, A or M/A, e.g. {(role == SupervisionRoles.Senior ? "SS" : "CC")} (M/A).";
                return null;
            }
            if (ValuationWords.Contains(joined)) return new Code(DutyKinds.Valuation, null, null, null);
            if (joined == "O") return new Code(DutyKinds.Other, null, null, null);
            if (original.Length > 100)
            {
                error = "the text is longer than 100 characters.";
                return null;
            }
            return new Code(DutyKinds.Other, null, null, original);
        }
    }
}
