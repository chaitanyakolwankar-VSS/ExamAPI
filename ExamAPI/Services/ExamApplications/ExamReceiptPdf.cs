using System.Text.Json;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Files;
using ExamAPI.Services.Payments;
using ExamAPI.Services.Report;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ExamAPI.Services.ExamApplications
{
    public enum ExamDocumentParts
    {
        /// <summary>Application form, then receipt (staff).</summary>
        Both,
        Form,
        Receipt,
    }

    /// <summary>
    /// The paid application's documents, in the layout colleges used before (the old LTCE exam form and receipt):
    /// page 1 the application form addressed to the Principal (photo, subjects with a tick for every part being sat,
    /// the declaration the student accepted, signatures); page 2 the fee receipt (amount in words, payment details).
    /// Everything is read from the saved application and the registration: nothing is recomputed.
    /// </summary>
    public static class ExamReceiptPdf
    {
        private const string Tick = "<svg viewBox=\"0 0 16 16\" xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M3 8.5l3 3 7-7\" stroke=\"#111111\" stroke-width=\"2.2\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/></svg>";
        private static readonly string[] HeadOrder = { "IA", "ESE", "TW", "OR", "ORAL", "PR", "PRACT", "PRACTICAL" };

        private sealed record SubjectLine(string Code, string Name, HashSet<string> Fresh, HashSet<string> All);

        public static async Task<byte[]> BuildAsync(ApplicationDbContext db, IFileStorage? storage, Guid applicationId, ExamDocumentParts parts = ExamDocumentParts.Both) =>
            (await ComposeAsync(db, storage, applicationId, parts)).GeneratePdf();

        /// <summary>The document itself (tests render it to images).</summary>
        public static async Task<IDocument> ComposeAsync(ApplicationDbContext db, IFileStorage? storage, Guid applicationId, ExamDocumentParts parts = ExamDocumentParts.Both)
        {
            var app = await db.ExamApplications.IgnoreQueryFilters().AsNoTracking()
                .Include(a => a.Subjects).Include(a => a.Student).Include(a => a.TargetExam)
                .FirstOrDefaultAsync(a => a.Id == applicationId && !a.IsDeleted)
                ?? throw new KeyNotFoundException("Application not found.");
            var collegeId = app.CollegeId ?? Guid.Empty;
            var branding = await CollegeBranding.LoadAsync(db, storage, collegeId);
            var center = await db.Colleges.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.CollegeId == collegeId).Select(c => c.CollegeCenter).FirstOrDefaultAsync();
            var wording = await ExamFormWording.LoadAsync(db, app.CollegeId);
            var photo = await CollegeBranding.ReadLogoAsync(storage, app.Student?.PhotoUrl);
            var course = app.TargetExam?.CourseId == null ? null : await db.CourseMasters.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.CourseId == app.TargetExam.CourseId).Select(c => c.Name).FirstOrDefaultAsync();
            var order = await db.PaymentOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.ApplicationId == app.Id && o.Status == PaymentOrderStatuses.Success)
                .OrderBy(o => o.ConfirmedAt).FirstOrDefaultAsync();
            var fee = string.IsNullOrEmpty(app.FeeBreakdownJson) ? null : JsonSerializer.Deserialize<FeeBreakdown>(app.FeeBreakdownJson);
            var (lines, seatNo, heads) = await SubjectsAsync(db, app);

            var data = new Doc(app, branding, center, wording, photo, course, order, fee, lines, seatNo, heads);
            return Document.Create(doc =>
            {
                if (parts != ExamDocumentParts.Receipt) doc.Page(p => FormPage(p, data));
                if (parts != ExamDocumentParts.Form) doc.Page(p => ReceiptPage(p, data));
            });
        }

        private sealed record Doc(
            ExamApplication App, CollegeBrandingInfo Branding, string? Center, ExamFormText Wording, byte[]? Photo, string? Course,
            PaymentOrder? Order, FeeBreakdown? Fee, List<SubjectLine> Lines, string? SeatNo, List<string> Heads)
        {
            public string StudentName => App.Student == null ? "" : StudentNames.Display(App.Student);
            public string ExamName => App.TargetExam?.Name ?? "";
            public string KindLabel => ExamApplicationKinds.Label(App.Kind);
            public string Semester => ReportService.SemesterLabel(App.SemesterId);
            public string PaidOn => App.PaidAt == null ? "" : PaymentService.Ist(App.PaidAt.Value);
            public string PaidDate => App.PaidAt == null ? "" : PaymentService.Ist(App.PaidAt.Value).Split(',')[0];
        }

        /// <summary>The applied subjects with the parts (heads) being sat, from the registration when it exists.</summary>
        private static async Task<(List<SubjectLine> Lines, string? SeatNo, List<string> Heads)> SubjectsAsync(ApplicationDbContext db, ExamApplication app)
        {
            var subjectIds = app.Subjects.Select(s => s.SubjectId).ToList();
            var nullableIds = subjectIds.Select(id => (Guid?)id).ToList();
            var subjects = await db.SubjectMasters.IgnoreQueryFilters().AsNoTracking()
                .Where(s => subjectIds.Contains(s.SubjectId))
                .Select(s => new { s.SubjectId, s.SubjectCode, s.Name, s.SortOrder })
                .ToListAsync();
            var target = await db.MarksMasters.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.ExamId == app.TargetExamId && m.StdMstId == app.StdMstId && !m.IsDeleted)
                .Select(m => new { m.MarksId, m.SeatNo })
                .FirstOrDefaultAsync();
            var marks = target == null ? new() : await db.StudentMarks.IgnoreQueryFilters().AsNoTracking()
                .Where(sm => sm.MarksId == target.MarksId && nullableIds.Contains(sm.SubjectId) && !sm.IsDeleted)
                .Select(sm => new { sm.SubjectId, sm.CreditsId, sm.Head, sm.IsCarryForward })
                .ToListAsync();
            var creditIds = marks.Where(m => m.CreditsId != null).Select(m => m.CreditsId!.Value).Distinct().ToList();
            var labels = (await db.SubjectCredits.IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.CreditsId != null && creditIds.Contains(c.CreditsId.Value))
                    .Select(c => new { c.CreditsId, c.Head, c.HeadType })
                    .ToListAsync())
                .GroupBy(c => (c.CreditsId ?? Guid.Empty, (c.Head ?? "").ToUpperInvariant()))
                .ToDictionary(g => g.Key, g => (g.First().HeadType ?? g.First().Head ?? "").Trim().ToUpperInvariant());
            string Label(Guid? creditsId, string? head)
            {
                var key = (creditsId ?? Guid.Empty, (head ?? "").ToUpperInvariant());
                return labels.TryGetValue(key, out var l) && l != "" ? l : (head ?? "").ToUpperInvariant();
            }

            var lines = subjects
                .OrderBy(s => s.SortOrder ?? int.MaxValue).ThenBy(s => s.SubjectCode)
                .Select(s =>
                {
                    var rows = marks.Where(m => m.SubjectId == s.SubjectId).ToList();
                    return new SubjectLine(s.SubjectCode, s.Name,
                        rows.Where(r => !r.IsCarryForward).Select(r => Label(r.CreditsId, r.Head)).ToHashSet(),
                        rows.Select(r => Label(r.CreditsId, r.Head)).ToHashSet());
                }).ToList();
            var heads = lines.SelectMany(l => l.All).Distinct()
                .OrderBy(h => Array.IndexOf(HeadOrder, h) is var i && i >= 0 ? i : HeadOrder.Length)
                .ThenBy(h => h).ToList();
            return (lines, target?.SeatNo, heads);
        }

        // =====================================================================

        private static void Header(IContainer container, Doc d, string title)
        {
            container.Column(h =>
            {
                h.Item().Row(r =>
                {
                    if (d.Branding.HasLogo) r.ConstantItem(56).Height(56).Image(d.Branding.Logo!).FitArea();
                    else r.ConstantItem(56);
                    r.RelativeItem().PaddingHorizontal(8).AlignMiddle().Column(c =>
                    {
                        c.Item().AlignCenter().Text(d.Branding.Name).FontSize(15).Bold();
                        if (!string.IsNullOrWhiteSpace(d.Branding.Address)) c.Item().AlignCenter().Text(d.Branding.Address).FontSize(8.5f);
                    });
                    r.ConstantItem(56);
                });
                h.Item().PaddingTop(6).LineHorizontal(0.75f);
                h.Item().PaddingTop(6).AlignCenter().Text(title).FontSize(12).Bold();
            });
        }

        private static void FormPage(PageDescriptor page, Doc d)
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(t => t.FontSize(9.5f));
            page.Header().Element(c => Header(c, d, $"APPLICATION FOR {d.KindLabel.ToUpperInvariant()} EXAMINATION"));

            page.Content().PaddingTop(8).Column(col =>
            {
                col.Spacing(7);

                // Exam and class on the left, photo on the right (as on the old form).
                col.Item().Row(r =>
                {
                    r.RelativeItem().Column(c =>
                    {
                        Pair(c, "Examination", d.ExamName);
                        Pair(c, "Branch", d.Course ?? "");
                        Pair(c, "Semester", d.Semester);
                        Pair(c, "Pattern", d.App.Pattern ?? "");
                    });
                    r.ConstantItem(84).Height(100).Border(0.75f).BorderColor(Colors.Grey.Medium).AlignCenter().AlignMiddle().Element(box =>
                    {
                        if (d.Photo != null) box.Image(d.Photo).FitArea();
                        else box.Text("Photo").FontColor(Colors.Grey.Medium);
                    });
                });

                col.Item().Column(c =>
                {
                    c.Item().Text("To,");
                    c.Item().Text("The Principal,").SemiBold();
                    c.Item().Text(d.Branding.Name);
                    if (!string.IsNullOrWhiteSpace(d.Center)) c.Item().Text(d.Center);
                });
                col.Item().Text("Sir / Madam,");
                col.Item().Text(text =>
                {
                    text.Span($"I request permission to appear for the above {d.KindLabel} examination. ");
                    if (d.App.PaidVia == PaidVia.Waived)
                        text.Span($"No fee is payable for my category ({d.App.Student?.Category}). Receipt no. {d.App.ReceiptNo}, dated {d.PaidDate}.");
                    else
                    {
                        text.Span("I remit herewith ");
                        text.Span($"Rs. {FeeCalculator.Money(d.App.AmountDue)}").SemiBold();
                        text.Span($" (receipt no. {d.App.ReceiptNo}, dated {d.PaidDate}).");
                    }
                });
                col.Item().Text(d.Wording.Declaration).Italic();

                col.Item().PaddingTop(2).Text("PERSONAL DETAILS").SemiBold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(80); c.RelativeColumn(); c.ConstantColumn(70); c.RelativeColumn(); });
                    void Cell(string label, string value)
                    {
                        t.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3).Text(label).FontColor(Colors.Grey.Darken2);
                        t.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3).Text(value).SemiBold();
                    }
                    Cell("Name", d.StudentName);
                    Cell("Student ID", d.App.Student?.StudentId ?? "");
                    Cell("Seat no.", d.SeatNo ?? "");
                    Cell("Category", d.App.Student?.Category ?? "");
                    Cell("Receipt no.", d.App.ReceiptNo ?? "");
                    Cell("Email", d.App.Student?.Email ?? "");
                });

                col.Item().PaddingTop(2).Text(d.App.Kind == ExamApplicationKinds.Revaluation ? "PAPERS FOR REVALUATION" : "SUBJECTS").SemiBold();
                col.Item().Table(t =>
                {
                    var heads = d.Heads.Count > 0 ? d.Heads : new List<string> { "APPLIED" };
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(22);
                        c.ConstantColumn(70);
                        c.RelativeColumn();
                        foreach (var _ in heads) c.ConstantColumn(38);
                    });
                    IContainer HeadCell(IContainer x) => x.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten4).Padding(3);
                    IContainer BodyCell(IContainer x) => x.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3);
                    t.Header(h =>
                    {
                        h.Cell().Element(HeadCell).Text("#").SemiBold();
                        h.Cell().Element(HeadCell).Text("Code").SemiBold();
                        h.Cell().Element(HeadCell).Text("Subject").SemiBold();
                        foreach (var head in heads) h.Cell().Element(HeadCell).AlignCenter().Text(head).SemiBold();
                    });
                    var n = 0;
                    foreach (var line in d.Lines)
                    {
                        n++;
                        t.Cell().Element(BodyCell).Text(n.ToString("00"));
                        t.Cell().Element(BodyCell).Text(line.Code);
                        t.Cell().Element(BodyCell).Text(line.Name);
                        foreach (var head in heads)
                        {
                            var ticked = d.Heads.Count == 0 || line.Fresh.Contains(head);
                            var cell = t.Cell().Element(BodyCell).AlignCenter().AlignMiddle();
                            if (ticked) cell.Width(10).Height(10).Svg(Tick);
                            else if (!line.All.Contains(head)) cell.Text("–").FontColor(Colors.Grey.Medium);
                        }
                    }
                });
                if (d.Heads.Count > 0)
                    col.Item().Text("A tick marks each part you sit again; parts you have cleared are carried forward.").FontSize(8).FontColor(Colors.Grey.Darken1);
                if (!string.IsNullOrWhiteSpace(d.Wording.Note))
                    col.Item().Text($"Note: {d.Wording.Note}").FontSize(8.5f);

                col.Item().PaddingTop(4).Text($"Declaration accepted online{(d.App.DeclaredAt != null ? " on " + PaymentService.Ist(d.App.DeclaredAt.Value) : "")}.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });

            page.Footer().Column(f =>
            {
                f.Item().PaddingBottom(4).Row(r =>
                {
                    r.RelativeItem().Text($"Place: {d.Center}");
                    r.RelativeItem().AlignRight().Text($"Date: {d.PaidDate}");
                });
                f.Item().Row(r =>
                {
                    foreach (var who in new[] { "Signature of the student", "Class advisor's signature", "Principal (seal)" })
                        r.RelativeItem().PaddingHorizontal(6).Column(c =>
                        {
                            c.Item().Height(34);
                            c.Item().LineHorizontal(0.5f);
                            c.Item().AlignCenter().Text(who).FontSize(8);
                        });
                });
            });
        }

        private static void ReceiptPage(PageDescriptor page, Doc d)
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(t => t.FontSize(10));
            page.Header().Element(c => Header(c, d, "PAYMENT RECEIPT"));

            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(8);
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(t => { t.Span("Receipt no.: "); t.Span(d.App.ReceiptNo ?? "").Bold(); });
                    r.RelativeItem().AlignRight().Text(t => { t.Span("Date: "); t.Span(d.PaidOn).SemiBold(); });
                });
                col.Item().Text(t => { t.Span("Received from "); t.Span(d.StudentName).Bold(); t.Span($" (Student ID {d.App.Student?.StudentId})"); });
                col.Item().Column(c =>
                {
                    Pair(c, "Course", $"{d.Course} · {d.Semester}");
                    Pair(c, "Examination", $"{d.ExamName} ({d.KindLabel})");
                    Pair(c, d.App.Kind == ExamApplicationKinds.Revaluation ? "Papers" : "Subjects", $"{d.Lines.Count}: {string.Join(", ", d.Lines.Select(l => l.Code))}");
                });

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(110); });
                    IContainer Cell(IContainer x) => x.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(5);
                    t.Header(h =>
                    {
                        h.Cell().Element(Cell).Background(Colors.Grey.Lighten4).Text("Particulars").SemiBold();
                        h.Cell().Element(Cell).Background(Colors.Grey.Lighten4).AlignRight().Text("Amount (Rs.)").SemiBold();
                    });
                    foreach (var line in d.Fee?.Lines ?? new List<FeeLine>())
                    {
                        t.Cell().Element(Cell).Text(line.Label.Replace("₹", "Rs. "));
                        t.Cell().Element(Cell).AlignRight().Text(FeeCalculator.Money(line.Amount));
                    }
                    t.Cell().Element(Cell).Text("Total").Bold();
                    t.Cell().Element(Cell).AlignRight().Text(FeeCalculator.Money(d.App.AmountDue)).Bold();
                });
                col.Item().Text(t => { t.Span("Amount in words: "); t.Span(IndianWords.Rupees(d.App.AmountDue)).SemiBold(); });

                col.Item().PaddingTop(4).Column(c =>
                {
                    switch (d.App.PaidVia)
                    {
                        case PaidVia.Online:
                            Pair(c, "Mode of payment", d.Order?.Mode ?? "Online");
                            Pair(c, "Order no.", d.Order?.OrderNo ?? "");
                            Pair(c, "Bank transaction ID", d.Order?.GatewayTxnId ?? "");
                            if (!string.IsNullOrWhiteSpace(d.Order?.Bank)) Pair(c, "Bank", d.Order!.Bank!);
                            Pair(c, "Payment status", "Success");
                            break;
                        case PaidVia.Office:
                            Pair(c, "Mode of payment", "At the college office");
                            Pair(c, "Office reference", d.App.PaymentReference ?? "");
                            break;
                        case PaidVia.Waived:
                            Pair(c, "Fee", $"Not payable for the student's category ({d.App.Student?.Category})");
                            break;
                    }
                });
            });

            page.Footer().AlignCenter().Text("Computer-generated receipt; no signature needed. Keep it until the result is declared.")
                .FontSize(8).FontColor(Colors.Grey.Darken1);
        }

        private static void Pair(ColumnDescriptor c, string label, string value) =>
            c.Item().PaddingVertical(1).Row(r =>
            {
                r.ConstantItem(110).Text(label).FontColor(Colors.Grey.Darken2);
                r.RelativeItem().Text(value).SemiBold();
            });
    }

    /// <summary>Rupee amounts in words, Indian system (lakh, crore): "Rupees Three Hundred Sixty Only".</summary>
    public static class IndianWords
    {
        private static readonly string[] Ones =
        {
            "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen",
            "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
        };
        private static readonly string[] Tens = { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };

        public static string Rupees(decimal amount)
        {
            var rupees = (long)decimal.Truncate(Math.Abs(amount));
            var paise = (int)decimal.Round((Math.Abs(amount) - rupees) * 100);
            var words = rupees == 0 ? "Zero" : Words(rupees);
            return $"Rupees {words}{(paise > 0 ? $" and {Words(paise)} Paise" : "")} Only";
        }

        public static string Words(long n)
        {
            if (n == 0) return "";
            var parts = new List<string>();
            void Add(long value, string unit) { if (value > 0) parts.Add(Below1000(value) + (unit == "" ? "" : " " + unit)); }
            Add(n / 10000000, "Crore");
            n %= 10000000;
            Add(n / 100000, "Lakh");
            n %= 100000;
            Add(n / 1000, "Thousand");
            n %= 1000;
            Add(n, "");
            return string.Join(" ", parts);
        }

        private static string Below1000(long n)
        {
            if (n >= 100000) return Words(n); // crores above 99: spell recursively
            var parts = new List<string>();
            if (n >= 100) { parts.Add(Ones[n / 100] + " Hundred"); n %= 100; }
            if (n >= 20) { parts.Add(Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "")); }
            else if (n > 0) parts.Add(Ones[n]);
            return string.Join(" ", parts);
        }
    }
}
