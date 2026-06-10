using ExamAPI.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ExamAPI.Services.Report.Documents
{
    public class BulkMarksheetDocument : IDocument
    {
        public IEnumerable<MarksheetReportDto> Models { get; }

        public BulkMarksheetDocument(IEnumerable<MarksheetReportDto> models)
        {
            Models = models;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            foreach (var model in Models)
            {
                container
                    .Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(30, Unit.Point);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                        page.Header().Element(c => ComposeHeader(c, model));
                        page.Content().Element(c => ComposeContent(c, model));
                        page.Footer().Element(c => ComposeFooter(c, model));
                    });
            }
        }

        void ComposeHeader(IContainer container, MarksheetReportDto model)
        {
            container.Column(column =>
            {
                column.Item().AlignCenter().Text("UNIVERSITY OF MUMBAI").FontSize(14).Bold();
                column.Item().AlignCenter().Text("LOKMANYA TILAK COLLEGE OF ENGINEERING").FontSize(16).Bold();
                column.Item().AlignCenter().Text("STATEMENT OF MARKS").FontSize(12).SemiBold().Underline();
                
                column.Item().PaddingTop(15).Row(row =>
                {
                    row.RelativeItem().Column(col => 
                    {
                        col.Item().Text(text => { text.Span("Name: ").Bold(); text.Span(model.StudentName); });
                        col.Item().Text(text => { text.Span("Program: ").Bold(); text.Span(model.ProgramName); });
                    });
                    
                    row.RelativeItem().Column(col => 
                    {
                        col.Item().AlignRight().Text(text => { text.Span("Seat No: ").Bold(); text.Span(model.SeatNo); });
                        col.Item().AlignRight().Text(text => { text.Span("PRN: ").Bold(); text.Span(model.PRN); });
                    });
                });
                
                column.Item().PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().Text(text => { text.Span("Examination: ").Bold(); text.Span(model.ExamName); });
                    row.RelativeItem().AlignRight().Text(text => { text.Span("Semester: ").Bold(); text.Span(model.Semester); });
                });
                
                column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Black);
            });
        }

        void ComposeContent(IContainer container, MarksheetReportDto model)
        {
            container.PaddingVertical(10).Column(column =>
            {
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(50); // Course Code
                        columns.RelativeColumn(3);  // Course Name
                        columns.RelativeColumn(1);  // Head 1
                        columns.RelativeColumn(1);  // Head 2
                        columns.RelativeColumn(1);  // Total
                        columns.RelativeColumn(1);  // Grd
                        columns.RelativeColumn(1);  // GP
                        columns.RelativeColumn(1);  // C
                        columns.RelativeColumn(1);  // CG
                    });

                    table.Header(header =>
                    {
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("Code").Bold();
                        header.Cell().Border(1).Padding(2).AlignLeft().Text("Course Name").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("TH/PR").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("IA/TW").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("Total").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("Grd").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("GP").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("C").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("CG").Bold();
                    });

                    foreach (var sub in model.Subjects)
                    {
                        table.Cell().Border(1).Padding(2).AlignCenter().Text(sub.SubjectCode);
                        table.Cell().Border(1).Padding(2).AlignLeft().Text(sub.SubjectName);
                        table.Cell().Border(1).Padding(2).AlignCenter().Text($"{sub.Head1Marks}{sub.Head1Grace}/{sub.Head1Max}");
                        table.Cell().Border(1).Padding(2).AlignCenter().Text($"{sub.Head2Marks}{sub.Head2Grace}/{sub.Head2Max}");
                        table.Cell().Border(1).Padding(2).AlignCenter().Text($"{sub.TotalObtained}/{sub.TotalMax}");
                        table.Cell().Border(1).Padding(2).AlignCenter().Text(sub.Grade);
                        table.Cell().Border(1).Padding(2).AlignCenter().Text(sub.GradePoint.ToString());
                        table.Cell().Border(1).Padding(2).AlignCenter().Text(sub.Credits.ToString());
                        table.Cell().Border(1).Padding(2).AlignCenter().Text(sub.EarnedGradePoints.ToString());
                    }
                });

                column.Item().PaddingTop(15).Table(table => 
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("Total Marks:\n").Bold(); text.Span($"{model.TotalObtained} / {model.TotalMax}"); });
                    table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("Credits:\n").Bold(); text.Span($"{model.CreditsEarned} / {model.TotalCredits}"); });
                    table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("SGPI:\n").Bold(); text.Span($"{model.SGPI}"); });
                    
                    if (model.CGPI.HasValue)
                    {
                        table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("CGPI:\n").Bold(); text.Span($"{model.CGPI}"); });
                    }
                    else
                    {
                        table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("CGPI:\n").Bold(); text.Span("-"); });
                    }

                    table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("Remark:\n").Bold(); text.Span(model.Remark); });
                });
            });
        }

        void ComposeFooter(IContainer container, MarksheetReportDto model)
        {
            container.PaddingTop(30).Row(row =>
            {
                row.RelativeItem().AlignLeft().Text($"Date: {model.ResultDate:dd/MM/yyyy}").FontSize(10);
                row.RelativeItem().AlignRight().Text("Principal / Exam Controller").FontSize(10).Bold();
            });
        }
    }
}
