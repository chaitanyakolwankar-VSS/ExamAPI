using System.Globalization;
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
    /// supervision duty letter or the examiner / moderator appointment letter. Only reads; the letters print what was
    /// written when they were saved (name, exam title, half, duties), plus the current designation, subject and branch.
    /// </summary>
    public class StaffLetterPdfService : IStaffLetterPdfService
    {
        private const float Margin = 15;   // mm
        private const float BodySize = 11;
        private static readonly string[] Roman = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };
        private static readonly char[] BadFileNameChars = Path.GetInvalidFileNameChars();

        private readonly ApplicationDbContext _context;
        private readonly IFileStorage? _storage;

        public StaffLetterPdfService(ApplicationDbContext context, IFileStorage? storage = null)
        {
            _context = context;
            _storage = storage;
        }

        /// <summary>What a letter prints besides its own fields.</summary>
        private sealed record LetterPage(StaffLetter Letter, StaffMaster? Staff, string? Designation, SubjectInfo? Subject, Branding Branding);

        private sealed record SubjectInfo(string Code, string Name, string Branch, string Semester, string Pattern);

        /// <summary>The college's letterhead, signatures, seal and CAP contact numbers.</summary>
        private sealed record Branding(CollegeBrandingInfo Info, byte[]? Seal, string? CapContact);

        public async Task<(byte[] Bytes, string FileName)> OneAsync(Guid letterId)
        {
            var letter = await _context.StaffLetters.AsNoTracking().Include(l => l.Duties)
                .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDeleted);
            if (letter == null) throw new KeyNotFoundException("Letter not found.");

            var pages = await PagesAsync(new List<StaffLetter> { letter });
            var name = $"{letter.LetterNo.Replace('/', '-')} {letter.StaffName}.pdf";
            return (Render(pages), CleanFileName(name));
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
            var designationIds = staff.Values.Where(s => s.DesignationId != null).Select(s => s.DesignationId!.Value).Distinct().ToList();
            var designations = await _context.StaffOptions.AsNoTracking().Where(o => designationIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name);

            var subjectIds = letters.Where(l => l.SubjectId != null).Select(l => l.SubjectId!.Value).Distinct().ToList();
            var subjects = (await _context.SubjectMasters.AsNoTracking().Include(s => s.Course)
                    .Where(s => subjectIds.Contains(s.SubjectId)).ToListAsync())
                .ToDictionary(s => s.SubjectId, s => new SubjectInfo(
                    s.SubjectCode, s.Name, s.Course?.Name ?? "", SemesterText(s.SemId, s.SemName), (s.Pattern ?? "").Trim()));

            var brandings = new Dictionary<Guid, Branding>();
            var pages = new List<LetterPage>();
            foreach (var letter in letters)
            {
                var collegeId = letter.CollegeId ?? _context.CurrentCollegeId ?? Guid.Empty;
                if (!brandings.TryGetValue(collegeId, out var branding))
                    brandings[collegeId] = branding = await BrandingAsync(collegeId);

                staff.TryGetValue(letter.StaffId, out var person);
                var designation = person?.DesignationId != null && designations.TryGetValue(person.DesignationId.Value, out var d) ? d : null;
                SubjectInfo? subject = null;
                if (letter.SubjectId != null) subjects.TryGetValue(letter.SubjectId.Value, out subject);
                pages.Add(new LetterPage(letter, person, designation, subject, branding));
            }
            return pages;
        }

        private async Task<Branding> BrandingAsync(Guid collegeId)
        {
            if (collegeId == Guid.Empty) return new Branding(CollegeBrandingInfo.Empty, null, null);

            var info = await CollegeBranding.LoadAsync(_context, _storage, collegeId);
            var sealUrl = await _context.Colleges.AsNoTracking().Where(c => c.CollegeId == collegeId).Select(c => c.SealUrl).FirstOrDefaultAsync();
            var seal = await CollegeBranding.ReadLogoAsync(_storage, sealUrl);
            var cap = await _context.CollegeSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.CollegeId == collegeId && !s.IsDeleted).Select(s => s.CapContact).FirstOrDefaultAsync();
            return new Branding(info, seal, string.IsNullOrWhiteSpace(cap) ? null : cap.Trim());
        }

        /// <summary>"Semester VI" from the subject's semester number; its own semester name when it has no number.</summary>
        private static string SemesterText(string? semId, string? semName)
        {
            var digits = new string((semId ?? "").Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n) && n > 0)
                return $"Semester {(n < Roman.Length ? Roman[n] : n.ToString(CultureInfo.InvariantCulture))}";
            return (semName ?? "").Trim();
        }

        // ------------------------------------------------------------------ layout

        private static byte[] Render(List<LetterPage> pages) => Build(pages).GeneratePdf();

        private static Document Build(List<LetterPage> pages) =>
            Document.Create(container =>
            {
                foreach (var page in pages)
                {
                    container.Page(p =>
                    {
                        p.Size(PageSizes.A4);
                        p.Margin(Margin, Unit.Millimetre);
                        p.PageColor(Colors.White);
                        p.DefaultTextStyle(x => x.FontSize(BodySize).FontFamily(Fonts.Arial).LineHeight(1.35f));

                        p.Header().Element(c => Header(c, page.Branding.Info));
                        if (page.Letter.LetterType == StaffLetterTypes.Supervision)
                        {
                            p.Content().Element(c => SupervisionBody(c, page));
                            p.Footer().Element(c => SupervisionSignatures(c, page.Branding));
                        }
                        else
                        {
                            p.Content().Element(c => AppointmentBody(c, page));
                        }
                    });
                }
            });

        /// <summary>The banner (full width), else the logo and the name, else the name centred and bold; a thin rule below.</summary>
        private static void Header(IContainer container, CollegeBrandingInfo info)
        {
            container.PaddingBottom(14).Column(column =>
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

        private static void SupervisionBody(IContainer container, LetterPage page)
        {
            var letter = page.Letter;
            container.Column(column =>
            {
                column.Item().AlignRight().Element(c => NumberAndDate(c, letter));

                column.Item().PaddingTop(14).Column(to =>
                {
                    to.Item().Text("To,");
                    to.Item().Text($"{letter.StaffName},").Bold();
                    if (!string.IsNullOrWhiteSpace(page.Designation)) to.Item().Text(page.Designation);
                });

                column.Item().PaddingTop(18).AlignCenter().Text("SUPERVISION DUTY").FontSize(14).Bold().Underline();

                Paragraph(column, t =>
                {
                    t.Span("You have been assigned ");
                    t.Span(letter.Role ?? "supervision").Bold();
                    t.Span(" duties for the ");
                    t.Span(letter.ExamTitle).Bold();
                    t.Span(" examinations");
                    if (!string.IsNullOrWhiteSpace(letter.Half)) t.Span($" ({letter.Half})");
                    t.Span(" on the following dates and times.");
                }, 16);

                column.Item().PaddingTop(12).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(64);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Element(HeaderCell).AlignCenter().Text("Sr. No.").Bold();
                        h.Cell().Element(HeaderCell).Text("Date").Bold();
                        h.Cell().Element(HeaderCell).Text("Time").Bold();
                    });
                    var no = 1;
                    foreach (var duty in letter.Duties.OrderBy(d => d.SortOrder))
                    {
                        table.Cell().Element(BodyCell).AlignCenter().Text(no++.ToString(CultureInfo.InvariantCulture));
                        table.Cell().Element(BodyCell).Text(duty.Date);
                        table.Cell().Element(BodyCell).Text(duty.Time);
                    }
                });

                Paragraph(column, t => t.Span("You are requested to report for duty one hour before the scheduled time."), 14);
            });
        }

        private static IContainer HeaderCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten3).PaddingVertical(5).PaddingHorizontal(8);

        private static IContainer BodyCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Grey.Darken1).PaddingVertical(5).PaddingHorizontal(8);

        private static void SupervisionSignatures(IContainer container, Branding branding)
        {
            container.PaddingTop(10).Row(row =>
            {
                row.ConstantItem(170).Element(c => Signatory(c, branding.Info.ControllerSignature, "Examination In-Charge"));
                row.RelativeItem().AlignCenter().AlignBottom().Height(70).Element(c =>
                {
                    if (branding.Seal is { Length: > 0 }) c.AlignCenter().AlignBottom().Image(branding.Seal).FitArea();
                });
                row.ConstantItem(170).Element(c => Signatory(c, branding.Info.PrincipalSignature, "Principal"));
            });
        }

        /// <summary>The signature image (when the college has one; otherwise blank space to sign by hand) above a rule and the title.</summary>
        private static void Signatory(IContainer container, byte[]? signature, string title)
        {
            container.Column(col =>
            {
                var slot = col.Item().Height(50);
                if (signature is { Length: > 0 })
                    slot.AlignCenter().AlignBottom().Image(signature).FitArea();
                col.Item().BorderTop(0.75f).PaddingTop(3).AlignCenter().Text(title).FontSize(10).Bold();
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
