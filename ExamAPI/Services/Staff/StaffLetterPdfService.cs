using System.Globalization;
using System.Text.RegularExpressions;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Files;
using ExamAPI.Services.Report;
using ExamAPI.Services.Report.Documents;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ExamAPI.Services.Staff
{
    /// <summary>
    /// Staff letters as PDF, made with QuestPDF like the marksheets: A4, the college banner (or name) at the top, then the
    /// supervision duty letter (an appointment order, one page for the person's whole duty chart) or the examiner /
    /// moderator appointment letter. Only reads; the letters print what was written when they were saved (name, chart or
    /// exam title, half, duty lines), plus the current designation, department, subject and branch.
    /// </summary>
    public class StaffLetterPdfService : IStaffLetterPdfService
    {
        private const float Margin = 15;   // mm
        private const float BodySize = 11;
        private static readonly string[] Roman = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };
        private static readonly char[] BadFileNameChars = Path.GetInvalidFileNameChars();

        // The supervision letter's colours (the same on every college's letter).
        private const string TitleBarColour = "#1F2A44";
        private const string ReportBackground = "#EAF5EC";
        private const string ReportBorder = "#6BAA75";

        private readonly ApplicationDbContext _context;
        private readonly IFileStorage? _storage;

        public StaffLetterPdfService(ApplicationDbContext context, IFileStorage? storage = null)
        {
            _context = context;
            _storage = storage;
        }

        /// <summary>What a letter prints besides its own fields.</summary>
        private sealed record LetterPage(StaffLetter Letter, StaffMaster? Staff, string? Designation, string? Department, SubjectInfo? Subject,
            Branding Branding, ChartInfo? Chart);

        private sealed record SubjectInfo(string Code, string Name, string Branch, string Semester, string Pattern);

        /// <summary>What a supervision letter says about the chart it belongs to: the academic year and the semesters examined on its days.</summary>
        internal sealed record ChartInfo(string AcademicYear, string Semesters);

        /// <summary>The college's letterhead, signatures, seal, CAP contact numbers and the supervision letter's texts.</summary>
        internal sealed record Branding(CollegeBrandingInfo Info, byte[]? Seal, string? CapContact, string ReportingPlace, IReadOnlyList<string> Guidelines);

        public async Task<(byte[] Bytes, string FileName)> OneAsync(Guid letterId)
        {
            var letter = await _context.StaffLetters.AsNoTracking().Include(l => l.Duties)
                .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDeleted);
            if (letter == null) throw new KeyNotFoundException("Letter not found.");

            var pages = await PagesAsync(new List<StaffLetter> { letter });
            var name = $"{letter.LetterNo.Replace('/', '-')} {letter.StaffName}.pdf";
            return (Render(pages), CleanFileName(name));
        }

        public async Task<(byte[] Bytes, string FileName)> ChartAsync(Guid dutyChartId)
        {
            var chart = await _context.DutyCharts.AsNoTracking().Where(c => c.Id == dutyChartId).Select(c => new { c.Name }).FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Duty chart not found.");
            var letters = (await _context.StaffLetters.AsNoTracking().Include(l => l.Duties)
                    .Where(l => !l.IsDeleted && l.DutyChartId == dutyChartId && l.LetterType == StaffLetterTypes.Supervision).ToListAsync())
                .OrderBy(l => l.SerialNo).ToList();
            if (letters.Count == 0) throw new KeyNotFoundException("There are no supervision letters for this duty chart.");

            var pages = await PagesAsync(letters);
            return (Render(pages), CleanFileName($"{chart.Name} duty letters.pdf"));
        }

        public async Task<(byte[] Bytes, string FileName)> ManyAsync(Guid examId, string kind)
        {
            var supervision = string.Equals((kind ?? "").Trim(), "supervision", StringComparison.OrdinalIgnoreCase);
            if (!supervision && !string.Equals((kind ?? "").Trim(), "appointment", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The kind of letter must be supervision or appointment.");

            var copyIds = await ExamGroups.CopyIdsAsync(_context, examId);
            if (copyIds.Count == 0) throw new KeyNotFoundException("Exam not found.");

            var query = _context.StaffLetters.AsNoTracking().Include(l => l.Duties)
                .Where(l => !l.IsDeleted && (l.ExamId != null && copyIds.Contains(l.ExamId.Value)));
            query = supervision
                ? query.Where(l => l.LetterType == StaffLetterTypes.Supervision)
                : query.Where(l => l.LetterType == StaffLetterTypes.Examiner || l.LetterType == StaffLetterTypes.Moderator);
            var letters = (await query.ToListAsync()).OrderBy(l => l.LetterType, StringComparer.Ordinal).ThenBy(l => l.SerialNo).ToList();
            if (letters.Count == 0)
                throw new KeyNotFoundException(supervision ? "There are no supervision letters for this exam." : "There are no appointment letters for this exam.");

            var pages = await PagesAsync(letters);
            var name = $"{letters[0].ExamTitle} {(supervision ? "Supervision" : "Appointment")} letters.pdf";
            return (Render(pages), CleanFileName(name));
        }

        private static string CleanFileName(string name)
        {
            var clean = new string(name.Select(c => BadFileNameChars.Contains(c) ? '-' : c).ToArray());
            return string.Join(' ', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        // ------------------------------------------------------------------ data

        private async Task<List<LetterPage>> PagesAsync(List<StaffLetter> letters)
        {
            var staffIds = letters.Select(l => l.StaffId).Distinct().ToList();
            var staff = await _context.StaffMasters.AsNoTracking().Where(s => staffIds.Contains(s.StaffId)).ToDictionaryAsync(s => s.StaffId);
            var optionIds = staff.Values.SelectMany(s => new[] { s.DesignationId, s.DepartmentId }).Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
            var options = await _context.StaffOptions.AsNoTracking().Where(o => optionIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name);

            var subjectIds = letters.Where(l => l.SubjectId != null).Select(l => l.SubjectId!.Value).Distinct().ToList();
            var subjects = (await _context.SubjectMasters.AsNoTracking().Include(s => s.Course)
                    .Where(s => subjectIds.Contains(s.SubjectId)).ToListAsync())
                .ToDictionary(s => s.SubjectId, s => new SubjectInfo(
                    s.SubjectCode, s.Name, s.Course?.Name ?? "", SemesterText(s.SemId, s.SemName), (s.Pattern ?? "").Trim()));

            var years = await AcademicYearsAsync(letters);
            var charts = new Dictionary<Guid, ChartInfo>();
            foreach (var chartId in letters.Where(l => l.DutyChartId != null).Select(l => l.DutyChartId!.Value).Distinct())
                charts[chartId] = await ChartInfoAsync(chartId, years);

            var brandings = new Dictionary<Guid, Branding>();
            var pages = new List<LetterPage>();
            foreach (var letter in letters)
            {
                var collegeId = letter.CollegeId ?? _context.CurrentCollegeId ?? Guid.Empty;
                if (!brandings.TryGetValue(collegeId, out var branding))
                    brandings[collegeId] = branding = await BrandingAsync(collegeId);

                staff.TryGetValue(letter.StaffId, out var person);
                string? Option(Guid? id) => id != null && options.TryGetValue(id.Value, out var name) ? name : null;
                SubjectInfo? subject = null;
                if (letter.SubjectId != null) subjects.TryGetValue(letter.SubjectId.Value, out subject);
                ChartInfo? chart = null;
                if (letter.DutyChartId != null) charts.TryGetValue(letter.DutyChartId.Value, out chart);
                pages.Add(new LetterPage(letter, person, Option(person?.DesignationId), Option(person?.DepartmentId), subject, branding, chart));
            }
            return pages;
        }

        /// <summary>The letters' academic years by id, as "2025-26".</summary>
        internal async Task<Dictionary<Guid, string>> AcademicYearsAsync(List<StaffLetter> letters)
        {
            var ids = letters.Select(l => l.AYID).Distinct().ToList();
            var rows = await _context.AcademicYears.AsNoTracking().Where(a => ids.Contains(a.AYID)).Select(a => new { a.AYID, a.ShortDuration, a.FullDuration }).ToListAsync();
            return rows.ToDictionary(a => a.AYID, a => ShortYear(string.IsNullOrWhiteSpace(a.ShortDuration) ? a.FullDuration : a.ShortDuration));
        }

        /// <summary>"2025-2026" becomes "2025-26"; anything else is printed as written.</summary>
        public static string ShortYear(string? year)
        {
            var text = (year ?? "").Trim();
            var match = Regex.Match(text, @"^(\d{4})\s*[-/–]\s*(\d{4})$");
            return match.Success ? $"{match.Groups[1].Value}-{match.Groups[2].Value[2..]}" : text;
        }

        /// <summary>The semesters (I, III, V &amp; VII) whose papers are timetabled on the chart's days, and the chart's academic year.</summary>
        internal async Task<ChartInfo> ChartInfoAsync(Guid chartId, Dictionary<Guid, string> years)
        {
            var chart = await _context.DutyCharts.AsNoTracking().Where(c => c.Id == chartId).Select(c => new { c.AYID }).FirstOrDefaultAsync();
            if (chart == null) return new ChartInfo("", "");

            var days = (await _context.DutyChartDays.AsNoTracking().Where(d => d.ChartId == chartId).Select(d => d.Date).ToListAsync())
                .Select(d => d.Date).ToHashSet();
            var rows = await _context.TimeTables.AsNoTracking()
                .Where(t => t.ExamId != null && t.SubjectId != null && t.Date != null && t.Date != ""
                            && t.Exam!.AcademicYearAYID == chart.AYID && t.Exam.RevaluationForExamId == null)
                .Select(t => new { t.SubjectId, Date = t.Date! }).ToListAsync();
            var subjectIds = rows.Where(r => days.Contains(ExamSlotOrder.Date(r.Date)) ).Select(r => r.SubjectId!.Value).Distinct().ToList();
            var semIds = await _context.SubjectMasters.AsNoTracking().Where(s => subjectIds.Contains(s.SubjectId)).Select(s => s.SemId).ToListAsync();
            var numbers = semIds.Select(SemesterNumber).Where(n => n is > 0 and < 13).Select(n => n!.Value).Distinct().OrderBy(n => n).ToList();
            var romans = numbers.Select(n => Roman[n]).ToList();
            var text = romans.Count switch
            {
                0 => "",
                1 => $"Semester {romans[0]}",
                _ => $"Semesters {string.Join(", ", romans.Take(romans.Count - 1))} & {romans[^1]}",
            };
            return new ChartInfo(years.GetValueOrDefault(chart.AYID, ""), text);
        }

        private static int? SemesterNumber(string? semId)
        {
            var digits = new string((semId ?? "").Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? n : null;
        }

        internal async Task<Branding> BrandingAsync(Guid collegeId)
        {
            if (collegeId == Guid.Empty) return new Branding(CollegeBrandingInfo.Empty, null, null, DutyLetterDefaults.ReportingPlace, DutyLetterDefaults.Guidelines);

            var info = await CollegeBranding.LoadAsync(_context, _storage, collegeId);
            var sealUrl = await _context.Colleges.AsNoTracking().Where(c => c.CollegeId == collegeId).Select(c => c.SealUrl).FirstOrDefaultAsync();
            var seal = await CollegeBranding.ReadLogoAsync(_storage, sealUrl);
            var settings = await _context.CollegeSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.CollegeId == collegeId && !s.IsDeleted).Select(s => new { s.CapContact, s.ReportingPlace, s.DutyGuidelines }).FirstOrDefaultAsync();

            return new Branding(info, seal,
                string.IsNullOrWhiteSpace(settings?.CapContact) ? null : settings.CapContact.Trim(),
                string.IsNullOrWhiteSpace(settings?.ReportingPlace) ? DutyLetterDefaults.ReportingPlace : settings.ReportingPlace.Trim(),
                DutyLetterDefaults.GuidelinesOf(settings?.DutyGuidelines).Take(10).ToList());
        }

        /// <summary>"Semester VI" from the subject's semester number; its own semester name when it has no number.</summary>
        private static string SemesterText(string? semId, string? semName)
        {
            var n = SemesterNumber(semId);
            if (n is > 0)
                return $"Semester {(n < Roman.Length ? Roman[n.Value] : n.Value.ToString(CultureInfo.InvariantCulture))}";
            return (semName ?? "").Trim();
        }

        // ------------------------------------------------------------------ layout

        /// <summary>One document per letter, merged, so each supervision letter says "Page 1 of 1".</summary>
        private static byte[] Render(List<LetterPage> pages) =>
            pages.Count == 1 ? Build(pages[0]).GeneratePdf() : Document.Merge(pages.Select(Build).ToArray()).GeneratePdf();

        private static Document Build(LetterPage page) =>
            Document.Create(container =>
            {
                container.Page(p =>
                {
                    p.Size(PageSizes.A4);
                    p.Margin(Margin, Unit.Millimetre);
                    p.PageColor(Colors.White);
                    p.DefaultTextStyle(x => x.FontSize(BodySize).FontFamily(Fonts.Arial).LineHeight(1.35f));

                    var supervision = page.Letter.LetterType == StaffLetterTypes.Supervision;
                    p.Header().Element(c => Header(c, page.Branding.Info, supervision ? 6 : 14));
                    if (supervision)
                    {
                        // A long duty list shrinks to stay on the one page.
                        p.Content().ScaleToFit().Element(c => SupervisionBody(c, page));
                        p.Footer().Element(c => SupervisionFooter(c, page.Branding));
                    }
                    else
                    {
                        p.Content().Element(c => AppointmentBody(c, page));
                    }
                });
            });

        /// <summary>The banner (full width), else the logo and the name, else the name centred and bold; a thin rule below.</summary>
        private static void Header(IContainer container, CollegeBrandingInfo info, float spaceBelow)
        {
            container.PaddingBottom(spaceBelow).Column(column =>
            {
                column.Item().Element(c => BrandedHeader.Compose(c, info.Banner, 70, info.Logo, 50, title =>
                    title.Column(col =>
                    {
                        col.Item().AlignCenter().Text(info.Name).FontSize(16).Bold();
                        if (!string.IsNullOrWhiteSpace(info.Address))
                            col.Item().AlignCenter().Text(info.Address).FontSize(9).FontColor(Colors.Grey.Darken2);
                    })));
                column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Darken2);
            });
        }

        private static string DateText(DateTime date) => date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

        /// <summary>"Letter No: ..." and "Date: ..." with the labels in bold.</summary>
        private static void NumberAndDate(IContainer container, StaffLetter letter)
        {
            container.Column(col =>
            {
                col.Item().Text(t => { t.Span("Letter No: ").Bold(); t.Span(letter.LetterNo); });
                col.Item().Text(t => { t.Span("Date: ").Bold(); t.Span(DateText(letter.LetterDate)); });
            });
        }

        private static void Paragraph(ColumnDescriptor column, Action<TextDescriptor> text, float spaceBefore = 10)
        {
            column.Item().PaddingTop(spaceBefore).Text(t =>
            {
                t.Justify();
                text(t);
            });
        }

        // ------------------------------------------------------------------ supervision

        /// <summary>The title of the order for a role: "CHIEF CONDUCTOR", "SENIOR EXAMINATION SUPERVISOR"...</summary>
        internal static string OrderTitle(string? role) => role switch
        {
            SupervisionRoles.ChiefConductor => "CHIEF CONDUCTOR",
            SupervisionRoles.Senior => "SENIOR EXAMINATION SUPERVISOR",
            SupervisionRoles.Junior => "JUNIOR EXAMINATION SUPERVISOR",
            SupervisionRoles.NonTeaching => "EXAMINATION DUTY — NON-TEACHING STAFF",
            _ => "EXAMINATION DUTY",
        };

        private static readonly string[] DateFormats = { "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "dd MMM yyyy", "d MMM yyyy" };

        /// <summary>A line's date as "10 NOV 2025 (Monday)"; text that is not a date is printed as it is.</summary>
        internal static string AssignedDate(string text)
        {
            if (!DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return text;
            return $"{date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()} ({date.ToString("dddd", CultureInfo.InvariantCulture)})";
        }

        private static bool IsDuty(StaffLetterDuty line) => line.Kind == DutyKinds.Duty;

        private static string ClockText(TimeSpan time) => DateTime.Today.Add(time).ToString("hh:mm tt", CultureInfo.InvariantCulture);

        /// <summary>When to report for each kind of session: one hour before the start of the duties ("09:30 AM", or one time per session).</summary>
        internal static string ReportingTimes(IEnumerable<StaffLetterDuty> lines)
        {
            var starts = lines.Where(IsDuty).Select(d => ExamSlotOrder.StartTime(d.Time)).Where(t => t != TimeSpan.MaxValue).Distinct().OrderBy(t => t).ToList();
            if (starts.Count == 0) return "";
            string Before(TimeSpan start) => ClockText(start - TimeSpan.FromHours(1));
            if (starts.Count == 1) return Before(starts[0]);

            var morning = starts.Where(t => t < TimeSpan.FromHours(12)).Select(Before).ToList();
            var afternoon = starts.Where(t => t >= TimeSpan.FromHours(12)).Select(Before).ToList();
            var parts = new List<string>();
            if (morning.Count > 0) parts.Add($"{string.Join(" / ", morning)} for the morning session");
            if (afternoon.Count > 0) parts.Add($"{string.Join(" / ", afternoon)} for the afternoon session");
            return string.Join("; ", parts);
        }

        private static void SupervisionBody(IContainer container, LetterPage page)
        {
            var letter = page.Letter;
            var lines = letter.Duties.Where(d => d.Kind != DutyKinds.RetiredValuation).OrderBy(d => d.SortOrder).ToList();
            var posting = string.Join(" — ", new[] { page.Designation, page.Department }.Where(p => !string.IsNullOrWhiteSpace(p)));
            var reporting = ReportingTimes(lines);

            container.Column(column =>
            {
                // Ref No and date in a light box.
                column.Item().Background(Colors.Grey.Lighten4).Border(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(5).PaddingHorizontal(8).Row(row =>
                {
                    row.RelativeItem().Text(t => { t.Span("Ref No: ").Bold(); t.Span(letter.LetterNo); });
                    row.AutoItem().Text(t => { t.Span("Date: ").Bold(); t.Span(letter.LetterDate.ToString("dd / MM / yyyy", CultureInfo.InvariantCulture)); });
                });

                // The title bar.
                column.Item().PaddingTop(8).Background(TitleBarColour).PaddingVertical(7).PaddingHorizontal(8).AlignCenter()
                    .Text($"APPOINTMENT ORDER: {OrderTitle(letter.Role)}").FontSize(12.5f).Bold().FontColor(Colors.White);

                column.Item().PaddingTop(12).Column(to =>
                {
                    to.Item().Text("TO,").Bold().FontSize(10);
                    to.Item().Text(letter.StaffName).Bold().FontSize(12.5f);
                    if (posting != "") to.Item().Text(posting).FontColor(Colors.Grey.Darken2);
                });

                Paragraph(column, t =>
                {
                    t.Span("You are hereby informed that you have been assigned duties as ");
                    t.Span(string.IsNullOrWhiteSpace(letter.Role) ? "Examination Supervisor" : letter.Role).Bold();
                    t.Span(" for the End-Semester Examinations");
                    if (!string.IsNullOrWhiteSpace(page.Chart?.Semesters)) t.Span($" ({page.Chart!.Semesters})");
                    var year = page.Chart?.AcademicYear ?? "";
                    if (!string.IsNullOrWhiteSpace(letter.Half))
                    {
                        t.Span(" during the ");
                        t.Span(letter.Half).Bold();
                        if (year != "")
                        {
                            t.Span(" of the Academic Year ");
                            t.Span(year).Bold();
                        }
                    }
                    else if (year != "")
                    {
                        t.Span(" during the Academic Year ");
                        t.Span(year).Bold();
                    }
                    t.Span(". The details of your duties are given below.");
                }, 10);

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(52);
                        c.RelativeColumn(3.1f);
                        c.RelativeColumn(4.2f);
                        c.RelativeColumn(2.6f);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Element(HeaderCell).AlignCenter().Text("Sr. No.").Bold().FontSize(10);
                        h.Cell().Element(HeaderCell).Text("Assigned Date").Bold().FontSize(10);
                        h.Cell().Element(HeaderCell).Text("Duty Session / Timing").Bold().FontSize(10);
                        h.Cell().Element(HeaderCell).Text("Role").Bold().FontSize(10);
                    });
                    var no = 1;
                    foreach (var line in lines)
                    {
                        var shade = no % 2 == 0 ? Colors.Grey.Lighten5 : Colors.White;
                        IContainer Cell(IContainer c) => BodyCell(c, shade);
                        table.Cell().Element(Cell).AlignCenter().Text(no++.ToString(CultureInfo.InvariantCulture)).FontSize(10);
                        table.Cell().Element(Cell).Text(AssignedDate(line.Date)).FontSize(10);
                        if (IsDuty(line))
                        {
                            // A session without a time is saved as "Morning session" / "Afternoon session": print just that.
                            var noTime = line.Time == DutySessions.MorningNoTime || line.Time == DutySessions.AfternoonNoTime;
                            var morning = line.Time == DutySessions.MorningNoTime || (!noTime && ExamSlotOrder.StartTime(line.Time) < TimeSpan.FromHours(12));
                            table.Cell().Element(Cell).Text(t =>
                            {
                                t.DefaultTextStyle(x => x.FontSize(10));
                                if (noTime) t.Span(line.Time).Bold();
                                else
                                {
                                    t.Span(morning ? "Morning" : "Afternoon").Bold();
                                    t.Span($" ({line.Time})");
                                }
                            });
                        }
                        else
                        {
                            table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(line.Time) ? "Full day" : line.Time).FontSize(10);
                        }
                        table.Cell().Element(Cell).Text(line.Role ?? "").FontSize(10);
                    }
                });

                // Where and when to report.
                column.Item().PaddingTop(10).Background(ReportBackground).Border(0.75f).BorderColor(ReportBorder).Padding(8).Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(10));
                    t.Span("Important Reporting Instruction: ").Bold();
                    t.Span($"You are requested to report to {page.Branding.ReportingPlace} exactly one hour prior");
                    // A time is not split over two lines.
                    if (reporting != "") t.Span($" ({reporting.Replace(" AM", " AM").Replace(" PM", " PM")})");
                    t.Span(" to the scheduled commencement of the examination for pre-exam verification and paper distribution.");
                });

                // Guidelines.
                column.Item().PaddingTop(10).Border(0.75f).BorderColor(Colors.Grey.Darken1).Column(box =>
                {
                    box.Item().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(8).Text("SUPERVISOR GUIDELINES & DIRECTIVES").Bold().FontSize(10);
                    box.Item().Padding(8).Column(list =>
                    {
                        list.Spacing(3);
                        foreach (var guideline in page.Branding.Guidelines)
                            list.Item().Row(row =>
                            {
                                row.ConstantItem(14).Text("•").FontSize(10);
                                row.RelativeItem().Text(guideline).FontSize(10);
                            });
                    });
                });
            });
        }

        private static IContainer HeaderCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6);

        private static IContainer BodyCell(IContainer c, string background) =>
            c.Border(0.5f).BorderColor(Colors.Grey.Darken1).Background(background).PaddingVertical(3.5f).PaddingHorizontal(6);

        /// <summary>The signatures (In-Charge left, Principal right, the seal between) and the confidential footer with the page number.</summary>
        private static void SupervisionFooter(IContainer container, Branding branding)
        {
            container.PaddingTop(8).Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.ConstantItem(170).Element(c => Signatory(c, branding.Info.ControllerSignature, "In-Charge", "Examination Cell"));
                    row.RelativeItem().AlignCenter().AlignBottom().Height(70).Element(c =>
                    {
                        if (branding.Seal is { Length: > 0 }) c.AlignCenter().AlignBottom().Image(branding.Seal).FitArea();
                    });
                    row.ConstantItem(170).Element(c => Signatory(c, branding.Info.PrincipalSignature, "Principal", branding.Info.Name));
                });

                column.Item().PaddingTop(8).BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingTop(3).Row(row =>
                {
                    row.RelativeItem().Text($"{branding.Info.Name} — Confidential Examination Duty Allocation Letter").FontSize(8).FontColor(Colors.Grey.Darken1);
                    row.AutoItem().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" of ");
                        t.TotalPages();
                    });
                });
            });
        }

        /// <summary>The signature image (when the college has one; otherwise blank space to sign by hand) above a rule, the title and, optionally, a line under it.</summary>
        private static void Signatory(IContainer container, byte[]? signature, string title, string? subtitle = null)
        {
            container.Column(col =>
            {
                var slot = col.Item().Height(50);
                if (signature is { Length: > 0 })
                    slot.AlignCenter().AlignBottom().Image(signature).FitArea();
                col.Item().BorderTop(0.75f).PaddingTop(3).AlignCenter().Text(title).FontSize(10).Bold();
                if (!string.IsNullOrWhiteSpace(subtitle)) col.Item().AlignCenter().Text(subtitle).FontSize(9).FontColor(Colors.Grey.Darken2);
            });
        }

        // ------------------------------------------------------------------ examiner and moderator appointment

        private static void AppointmentBody(IContainer container, LetterPage page)
        {
            var letter = page.Letter;
            var moderator = letter.LetterType == StaffLetterTypes.Moderator;
            var role = moderator ? "Moderator" : "Examiner";
            var person = page.Staff;
            var where = person is { IsExternal: true }
                ? string.Join(", ", new[] { person.ExternalCollege, person.ExternalCity }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()))
                : page.Branding.Info.Name;

            container.Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("CONFIDENTIAL").Bold();
                    row.ConstantItem(220).AlignRight().Element(c => NumberAndDate(c, letter));
                });

                column.Item().PaddingTop(14).Column(to =>
                {
                    to.Item().Text("To,");
                    to.Item().Text($"{letter.StaffName},").Bold();
                    if (where != "") to.Item().Text(where);
                });

                column.Item().PaddingTop(16).Text($"Sub: Appointment as {role} for {letter.ExamTitle} Examination.").Bold();
                column.Item().PaddingTop(12).Text("Dear Sir / Madam,");

                Paragraph(column, t =>
                {
                    var subject = page.Subject;
                    t.Span($"We are pleased to appoint you as {(moderator ? "a Moderator" : "an Examiner")}");
                    if (subject != null)
                    {
                        t.Span(" for the subject ");
                        t.Span($"{subject.Code} {subject.Name}").Bold();
                        if (subject.Semester != "")
                        {
                            t.Span(" of ");
                            t.Span(subject.Semester).Bold();
                        }
                        if (subject.Pattern != "") t.Span($" ({subject.Pattern})");
                        if (subject.Branch != "")
                        {
                            t.Span(" of ");
                            t.Span(subject.Branch).Bold();
                        }
                    }
                    t.Span(" for the ");
                    t.Span(letter.ExamTitle).Bold();
                    t.Span(" examination.");
                }, 12);

                var contact = page.Branding.CapContact;
                Paragraph(column, t => t.Span("You are requested to get in touch immediately regarding the assessment work at the CAP centre"
                    + (contact != null ? $" on {contact}" : "") + "."));
                Paragraph(column, t => t.Span("Please complete the assessment / moderation work within the stipulated time (10 days) as directed by the CAP co-ordinator, "
                    + "as required by Section 72 of the Maharashtra Universities Act, 1994."));
                Paragraph(column, t => t.Span("Take utmost care while totalling the marks to avoid any mistake."));
                Paragraph(column, t => t.Span("Please follow the instructions and guidelines for assessment and moderation given by the University of Mumbai."));
                Paragraph(column, t =>
                {
                    t.Span("Your attention is invited to Clause 32(5)(g) of the MU Act, 1994: ");
                    t.Span("\"It shall be obligatory on every teacher of the college to render necessary assistance and service for completing the exam work. "
                        + "If any teacher fails to comply with the order of the college in this respect, it shall be misconduct and the teacher shall be liable for disciplinary action.\"").Italic();
                });

                column.Item().PaddingTop(18).Text("Yours faithfully,");
                column.Item().PaddingTop(4).Width(190).Element(c => Signatory(c, page.Branding.Info.PrincipalSignature, "Principal / CAP Director"));
            });
        }
    }
}
