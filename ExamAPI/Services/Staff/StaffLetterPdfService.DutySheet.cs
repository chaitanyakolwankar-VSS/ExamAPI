using System.Globalization;
using System.Text.RegularExpressions;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Report.Documents;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Elements;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ExamAPI.Services.Staff
{
    /// <summary>
    /// The daily duty sheet ("Examination Daily Duty Report") of a duty chart: for each day and session, everyone on duty in
    /// role order with empty Block No, Reporting Time and Sign columns. It is printed for the control room and signed on the
    /// day, laid out like the sheet the exam office made by hand (owner 2026-11-13). Made from the saved chart.
    /// </summary>
    public partial class StaffLetterPdfService
    {
        // The hand-made sheet's colours: green for the Chief Conductor and Senior Supervisors, yellow for the peons.
        private const string LeadFill = "#C6E0B4";
        private const string NonTeachingFill = "#FFF2A8";

        /// <summary>One session of the sheet: the people on duty, Chief Conductor first.</summary>
        internal sealed record SheetSession(DateTime Date, bool Morning, string? Time, string Classes, List<SheetRow> Rows);

        /// <summary>One line: the person and the duty printed for them (the role, or a non-teaching person's designation).</summary>
        internal sealed record SheetRow(string Name, string Duty, string Role);

        public async Task<(byte[] Bytes, string FileName)> DutySheetAsync(Guid dutyChartId, DateTime? date)
        {
            var chart = await _context.DutyCharts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == dutyChartId)
                ?? throw new KeyNotFoundException("Duty chart not found.");
            var sessions = await DutySheetSessionsAsync(chart, date);
            if (sessions.Count == 0)
                throw new KeyNotFoundException(date == null
                    ? "Nobody is on duty in this exam period yet. Mark duties and save the chart first."
                    : $"Nobody is on duty on {date.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}.");

            var branding = await BrandingAsync(chart.CollegeId ?? _context.CurrentCollegeId ?? Guid.Empty);
            var year = await _context.AcademicYears.AsNoTracking().Where(a => a.AYID == chart.AYID)
                .Select(a => a.ShortDuration ?? a.FullDuration).FirstOrDefaultAsync();
            var title = DutySheetTitle(chart.Half, year);

            void SheetPage(PageDescriptor p, Action<IContainer> content)
            {
                p.Size(PageSizes.A4);
                p.Margin(12, Unit.Millimetre);
                p.PageColor(Colors.White);
                p.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));
                p.Header().Column(col =>
                {
                    col.Item().Element(c => Header(c, branding.Info, 4));
                    col.Item().PaddingBottom(8).AlignCenter().Text(title).FontSize(12).SemiBold();
                });
                p.Content().Element(content);
                p.Footer().PaddingTop(6).Row(row =>
                {
                    row.ConstantItem(170).Column(sign =>
                    {
                        sign.Item().PaddingTop(18).LineHorizontal(0.75f);
                        sign.Item().AlignCenter().Text("Signature of Chief Conductor").FontSize(9);
                    });
                    row.RelativeItem().AlignRight().AlignBottom().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" of ");
                        t.TotalPages();
                    });
                });
            }

            // First pass: measure each session on the real page, so a session a little too long for one page can be
            // drawn tighter instead of running onto a second page (owner 2026-11-13, the 17th's afternoon split in two).
            var measure = new SheetMeasure(sessions);
            Document.Create(container => container.Page(p => SheetPage(p, c => c.Dynamic(measure)))).GeneratePdf();

            var bytes = Document.Create(container => container.Page(p => SheetPage(p, c => c.Column(col =>
            {
                col.Spacing(16);
                // Each session starts on a new page when it does not fit below the previous one. A session too long
                // even when drawn tightest runs on, with its heading row repeated.
                for (var i = 0; i < sessions.Count; i++)
                {
                    var (session, density) = (sessions[i], measure.Density[i]);
                    col.Item().PreventPageBreak().Element(c => SheetSessionBlock(c, session, density));
                }
            })))).GeneratePdf();
            var name = date == null ? $"{chart.Name} duty sheet.pdf" : $"{chart.Name} duty sheet {date.Value.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}.pdf";
            return (bytes, CleanFileName(name));
        }

        /// <summary>"Examination Daily Duty Report FH2026": FH / SH for the chart's half, then the academic year's first year.</summary>
        internal static string DutySheetTitle(string? half, string? academicYear)
        {
            var code = (half ?? "").Trim().ToLowerInvariant() switch
            {
                "first half" => "FH",
                "second half" => "SH",
                _ => ""
            };
            var year = Regex.Match(academicYear ?? "", @"\d{4}").Value;
            var suffix = code + year;
            return suffix == "" ? "Examination Daily Duty Report" : $"Examination Daily Duty Report {suffix}";
        }

        /// <summary>The sessions of the sheet (one day, or every day of the chart), each with its people in role order, then chart order.</summary>
        internal async Task<List<SheetSession>> DutySheetSessionsAsync(DutyChart chart, DateTime? date)
        {
            var days = await _context.DutyChartDays.AsNoTracking()
                .Where(d => d.ChartId == chart.Id && (date == null || d.Date == date.Value.Date)).OrderBy(d => d.Date).ToListAsync();
            var dates = days.Select(d => d.Date.Date).ToHashSet();
            var entries = (await _context.DutyChartEntries.AsNoTracking()
                    .Where(e => e.ChartId == chart.Id && e.Kind == DutyKinds.Duty).ToListAsync())
                .Where(e => dates.Contains(e.Date.Date)).ToList();
            if (entries.Count == 0) return new List<SheetSession>();

            var order = (await _context.DutyChartMembers.AsNoTracking().Where(m => m.ChartId == chart.Id).ToListAsync())
                .GroupBy(m => m.StaffId).ToDictionary(g => g.Key, g => g.Min(m => m.SortOrder));
            var staffIds = entries.Select(e => e.StaffId).Distinct().ToList();
            var staff = await _context.StaffMasters.AsNoTracking().Where(s => staffIds.Contains(s.StaffId)).ToDictionaryAsync(s => s.StaffId);
            var designationIds = staff.Values.Where(s => s.DesignationId != null).Select(s => s.DesignationId!.Value).Distinct().ToList();
            var designations = await _context.StaffOptions.AsNoTracking().Where(o => designationIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name);
            var classes = await SheetClassesAsync(chart, dates);
            var rank = SupervisionRoles.All.ToList();
            int Rank(string? role) => role != null && rank.Contains(role) ? rank.IndexOf(role) : rank.Count;

            var sessions = new List<SheetSession>();
            foreach (var day in days)
            {
                foreach (var morning in new[] { true, false })
                {
                    var rows = entries
                        .Where(e => e.Date.Date == day.Date.Date && (e.Session == DutySessions.Both || e.Session == (morning ? DutySessions.Morning : DutySessions.Afternoon)))
                        .Select(e => (Entry: e, Person: staff.GetValueOrDefault(e.StaffId)))
                        .OrderBy(x => Rank(x.Entry.Role))
                        .ThenBy(x => order.TryGetValue(x.Entry.StaffId, out var n) ? n : int.MaxValue)
                        .ThenBy(x => x.Person?.FirstName, StringComparer.OrdinalIgnoreCase)
                        .Select(x =>
                        {
                            var person = x.Person;
                            var name = person == null ? "" : StaffService.Letter(person.Title, person.FirstName, person.MiddleName, person.LastName);
                            var designation = person?.DesignationId != null ? designations.GetValueOrDefault(person.DesignationId.Value) : null;
                            // A non-teaching person's duty is their own designation ("Peon", "Water man"), as on the hand-made sheet.
                            var duty = x.Entry.Role == SupervisionRoles.NonTeaching && !string.IsNullOrWhiteSpace(designation) ? designation.Trim() : x.Entry.Role ?? "";
                            return new SheetRow(name, duty, x.Entry.Role ?? "");
                        })
                        .ToList();
                    if (rows.Count == 0) continue;
                    sessions.Add(new SheetSession(day.Date.Date, morning, morning ? day.Morning : day.Afternoon,
                        classes.GetValueOrDefault((day.Date.Date, morning), ""), rows));
                }
            }
            return sessions;
        }

        /// <summary>
        /// The semesters examined in each session of the days, from the timetables of the chart's academic year ("Sem III",
        /// or "Sem III, Sem V" when two semesters write that session). The branches are left out (owner 2026-11-13: one
        /// session can hold every branch, which made the line too long).
        /// </summary>
        private async Task<Dictionary<(DateTime Date, bool Morning), string>> SheetClassesAsync(DutyChart chart, HashSet<DateTime> dates)
        {
            var examIds = await _context.Exams.AsNoTracking()
                .Where(e => e.AcademicYearAYID == chart.AYID && e.RevaluationForExamId == null).Select(e => e.ExamId).ToListAsync();
            var rows = (await _context.TimeTables.AsNoTracking()
                    .Where(t => t.ExamId != null && examIds.Contains(t.ExamId.Value) && t.SubjectId != null && t.Date != null && t.Date != "" && t.Time != null && t.Time != "")
                    .Select(t => new { Date = t.Date!, Time = t.Time!, SubjectId = t.SubjectId!.Value }).ToListAsync())
                .Select(r => new { Date = ExamSlotOrder.Date(r.Date).Date, Morning = ExamSlotOrder.StartTime(r.Time) < TimeSpan.FromHours(12), r.SubjectId })
                .Where(r => dates.Contains(r.Date)).ToList();
            if (rows.Count == 0) return new();

            var subjectIds = rows.Select(r => r.SubjectId).Distinct().ToList();
            var semesters = await _context.SubjectMasters.AsNoTracking().Where(s => subjectIds.Contains(s.SubjectId))
                .ToDictionaryAsync(s => s.SubjectId, s => SemesterNumber(s.SemId));

            return rows.GroupBy(r => (r.Date, r.Morning)).ToDictionary(g => g.Key, g => string.Join(", ",
                g.Select(r => semesters.GetValueOrDefault(r.SubjectId)).Where(n => n is > 0).Select(n => n!.Value).Distinct().Order()
                    .Select(n => $"Sem {(n < Roman.Length ? Roman[n] : n.ToString(CultureInfo.InvariantCulture))}")));
        }

        /// <summary>How tight a session too long for one page may be drawn: rows and text at 75% of normal, still signable.</summary>
        internal const float MinSheetDensity = 0.75f;

        /// <summary>
        /// Measures every session against an empty page and keeps the largest density (1, 0.95 ... 0.75) at which it fits
        /// on one page. A session that fits as it is, or does not fit even at the tightest, stays at 1.
        /// </summary>
        internal sealed class SheetMeasure(List<SheetSession> sessions) : IDynamicComponent
        {
            public float[] Density { get; } = Enumerable.Repeat(1f, sessions.Count).ToArray();

            public DynamicComponentComposeResult Compose(DynamicContext context)
            {
                var width = context.AvailableSize.Width;
                var room = context.AvailableSize.Height - 2;
                float Height(SheetSession s, float d) =>
                    context.CreateElement(c => c.Width(width).Element(x => SheetSessionBlock(x, s, d))).Size.Height;

                for (var i = 0; i < sessions.Count; i++)
                {
                    Density[i] = 1f;
                    if (Height(sessions[i], 1f) <= room) continue;
                    for (var step = 19; step >= (int)Math.Round(MinSheetDensity * 20); step--)
                    {
                        var d = step / 20f;
                        if (Height(sessions[i], d) > room) continue;
                        Density[i] = d;
                        break;
                    }
                }
                return new DynamicComponentComposeResult { Content = context.CreateElement(_ => { }), HasMoreContent = false };
            }
        }

        internal static void SheetSessionBlock(IContainer container, SheetSession session, float density = 1f)
        {
            IContainer Cell(IContainer c) => SheetCell(c, density);
            container.DefaultTextStyle(x => x.FontSize(10 * density)).Column(col =>
            {
                col.Item().PaddingBottom(3 * density).Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span("Session - ").SemiBold();
                        t.Span(session.Morning ? "Morning" : "Afternoon").SemiBold();
                        if (!string.IsNullOrWhiteSpace(session.Time)) t.Span($" ({KeepTimesTogether(session.Time)})");
                        if (session.Classes != "") t.Span($"   {session.Classes}").SemiBold();
                    });
                    row.ConstantItem(150).AlignRight().Text($"Date : {session.Date.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture)}").SemiBold();
                });
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(44);
                        c.RelativeColumn(3.3f);
                        c.RelativeColumn(2.1f);
                        c.RelativeColumn(1.1f);
                        c.RelativeColumn(1.3f);
                        c.RelativeColumn(1.4f);
                    });
                    table.Header(h =>
                    {
                        foreach (var heading in new[] { "Sr. No", "Name", "Duty Allotted", "Block No", "Reporting Time", "Sign" })
                            h.Cell().Element(Cell).AlignCenter().Text(heading).SemiBold();
                    });
                    var no = 1;
                    foreach (var line in session.Rows)
                    {
                        var lead = line.Role == SupervisionRoles.ChiefConductor || line.Role == SupervisionRoles.Senior;
                        var fill = lead ? LeadFill : line.Role == SupervisionRoles.NonTeaching ? NonTeachingFill : null;
                        table.Cell().Element(Cell).AlignCenter().Text(no++.ToString(CultureInfo.InvariantCulture));
                        table.Cell().Element(c => fill == null ? Cell(c) : Cell(c.Background(fill))).Text(line.Name);
                        var duty = table.Cell().Element(Cell).AlignCenter().Text(line.Duty);
                        if (lead) duty.Bold();
                        table.Cell().Element(Cell).Text("");
                        table.Cell().Element(Cell).Text("");
                        table.Cell().Element(Cell).Text("");
                    }
                });
            });
        }

        /// <summary>A bordered cell tall enough to sign in (a little shorter for a tightened session).</summary>
        private static IContainer SheetCell(IContainer c, float density) =>
            c.Border(0.75f).BorderColor(Colors.Black).MinHeight(20 * density).PaddingHorizontal(5).PaddingVertical(3 * density).AlignMiddle();
    }
}
