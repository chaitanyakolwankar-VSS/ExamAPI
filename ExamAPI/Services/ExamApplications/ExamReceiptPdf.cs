using System.Text.Json;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Payments;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ExamAPI.Services.ExamApplications
{
    /// <summary>
    /// One A4 page: the fee receipt and the student's application (the subjects applied for), for the student's link and
    /// for staff. Everything is read from the saved application: nothing is recomputed.
    /// </summary>
    public static class ExamReceiptPdf
    {
        public static async Task<byte[]> BuildAsync(ApplicationDbContext db, Guid applicationId)
        {
            var app = await db.ExamApplications.IgnoreQueryFilters().AsNoTracking()
                .Include(a => a.Subjects).Include(a => a.Student).Include(a => a.TargetExam)
                .FirstOrDefaultAsync(a => a.Id == applicationId && !a.IsDeleted)
                ?? throw new KeyNotFoundException("Application not found.");
            var college = await db.Colleges.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.CollegeId == app.CollegeId).Select(c => new { c.Name, c.CollegeCenter }).FirstOrDefaultAsync();
            var subjectIds = app.Subjects.Select(s => (Guid?)s.SubjectId).ToList();
            var subjects = await db.SubjectMasters.IgnoreQueryFilters().AsNoTracking()
                .Where(s => subjectIds.Contains((Guid?)s.SubjectId))
                .Select(s => new { s.SubjectId, s.SubjectCode, SubjectName = s.Name })
                .ToListAsync();
            var course = app.TargetExam?.CourseId == null ? null : await db.CourseMasters.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.CourseId == app.TargetExam.CourseId).Select(c => c.Name).FirstOrDefaultAsync();
            var order = await db.PaymentOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.ApplicationId == app.Id && o.Status == PaymentOrderStatuses.Success)
                .OrderBy(o => o.ConfirmedAt).FirstOrDefaultAsync();
            var fee = string.IsNullOrEmpty(app.FeeBreakdownJson) ? null : JsonSerializer.Deserialize<FeeBreakdown>(app.FeeBreakdownJson);

            var studentName = app.Student == null ? "" : StudentNames.Display(app.Student);
            var examName = $"{app.TargetExam?.Name} ({ExamApplicationKinds.Label(app.Kind)})";
            var paidAt = app.PaidAt == null ? "" : PaymentService.Ist(app.PaidAt.Value);
            var how = app.PaidVia switch
            {
                PaidVia.Online => order == null ? "Online" : $"Online · order {order.OrderNo}" + (order.GatewayTxnId != null ? $" · transaction {order.GatewayTxnId}" : "") + (order.Mode != null ? $" · {order.Mode}" : ""),
                PaidVia.Office => $"At the college office · reference {app.PaymentReference}",
                PaidVia.Waived => "No fee (category)",
                _ => app.PaidVia ?? "",
            };

            return Document.Create(doc => doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(h =>
                {
                    h.Item().AlignCenter().Text(college?.Name ?? "").FontSize(15).Bold();
                    if (!string.IsNullOrWhiteSpace(college?.CollegeCenter)) h.Item().AlignCenter().Text(college.CollegeCenter).FontSize(9);
                    h.Item().PaddingTop(6).AlignCenter().Text("Exam application and fee receipt").FontSize(12).SemiBold();
                    h.Item().PaddingTop(6).LineHorizontal(0.75f);
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.ConstantColumn(110); c.RelativeColumn(); c.ConstantColumn(90); c.RelativeColumn(); });
                        void Pair(string label, string value)
                        {
                            t.Cell().PaddingVertical(2).Text(label).FontColor(Colors.Grey.Darken2);
                            t.Cell().PaddingVertical(2).Text(value).SemiBold();
                        }
                        Pair("Receipt no.", app.ReceiptNo ?? "");
                        Pair("Date", paidAt);
                        Pair("Student", studentName);
                        Pair("Student ID", app.Student?.StudentId ?? "");
                        Pair("Exam", examName);
                        Pair("Class", $"{course} {app.SemesterId}".Trim());
                        Pair("Pattern", app.Pattern ?? "");
                        Pair("Category", app.Student?.Category ?? "");
                    });

                    col.Item().PaddingTop(4).Text(app.Kind == ExamApplicationKinds.Revaluation ? "Papers applied for" : "Subjects applied for").SemiBold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.ConstantColumn(30); c.ConstantColumn(90); c.RelativeColumn(); });
                        t.Header(hd =>
                        {
                            foreach (var title in new[] { "#", "Code", "Subject" })
                                hd.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(title).SemiBold();
                        });
                        var n = 0;
                        foreach (var s in subjects.OrderBy(s => s.SubjectCode))
                        {
                            n++;
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(n.ToString());
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(s.SubjectCode ?? "");
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(s.SubjectName ?? "");
                        }
                    });

                    col.Item().PaddingTop(4).Text("Fee").SemiBold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(100); });
                        foreach (var line in fee?.Lines ?? new List<FeeLine>())
                        {
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(line.Label);
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text("₹" + FeeCalculator.Money(line.Amount));
                        }
                        t.Cell().Padding(4).Text("Total paid").Bold();
                        t.Cell().Padding(4).AlignRight().Text("₹" + FeeCalculator.Money(app.AmountDue)).Bold();
                    });
                    col.Item().Text($"Paid: {how}").FontSize(9);
                });

                page.Footer().AlignCenter().Text("Computer-generated receipt; no signature needed. Keep it until the result is declared.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            })).GeneratePdf();
        }
    }
}
