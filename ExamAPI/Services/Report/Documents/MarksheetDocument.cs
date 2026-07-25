using ExamAPI.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ExamAPI.Services.Report.Documents
{
    public class MarksheetDocument : IDocument
    {
        public MarksheetReportDto Model { get; }

        public MarksheetDocument(MarksheetReportDto model)
        {
            Model = model;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container
                .Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30, Unit.Point);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeContent);
                    page.Footer().Element(ComposeFooter);
                });
        }

        void ComposeHeader(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().AlignCenter().Text("UNIVERSITY OF MUMBAI").FontSize(11).Bold().FontColor(Colors.Grey.Darken2);
                column.Item().AlignCenter().Text("ENGINEERING COLLEGE").FontSize(15).Bold().FontColor(Colors.Blue.Darken3);
                column.Item().AlignCenter().Text("STATEMENT OF MARKS").FontSize(12).Bold().FontColor(Colors.Grey.Darken4);
                
                column.Item().PaddingTop(10).Background(Colors.Grey.Lighten4).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(8).Row(row =>
                {
                    row.RelativeItem().Column(col => 
                    {
                        col.Item().Text(text => { text.Span("Name: ").Bold().FontSize(9); text.Span(Model.StudentName).FontSize(9); });
                        col.Item().Text(text => { text.Span("Program: ").Bold().FontSize(9); text.Span(Model.ProgramName).FontSize(9); });
                        col.Item().Text(text => { text.Span("Examination: ").Bold().FontSize(9); text.Span(Model.ExamName).FontSize(9); });
                    });
                    
                    row.RelativeItem().Column(col => 
                    {
                        col.Item().AlignRight().Text(text => { text.Span("Seat No: ").Bold().FontSize(9); text.Span(Model.SeatNo).FontSize(9); });
                        col.Item().AlignRight().Text(text => { text.Span("PRN: ").Bold().FontSize(9); text.Span(Model.PRN).FontSize(9); });
                        col.Item().AlignRight().Text(text => { text.Span("Semester: ").Bold().FontSize(9); text.Span(Model.Semester).FontSize(9); });
                    });
                });
                
                column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            });
        }

        void ComposeContent(IContainer container)
        {
            container.PaddingVertical(10).Column(column =>
            {
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(50); // Course Code
                        columns.RelativeColumn(3);  // Course Name
                        columns.RelativeColumn(2.2f); // Configured heads
                        columns.RelativeColumn(1.2f); // Total
                        columns.RelativeColumn(0.8f); // Grd
                        columns.RelativeColumn(0.8f); // GP
                        columns.RelativeColumn(0.8f); // C
                        columns.RelativeColumn(0.8f); // CG
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Blue.Darken3).Padding(5).AlignCenter().Text("Code").Bold().FontColor(Colors.White).FontSize(9);
                        header.Cell().Background(Colors.Blue.Darken3).Padding(5).AlignLeft().Text("Course Name").Bold().FontColor(Colors.White).FontSize(9);
                        header.Cell().Background(Colors.Blue.Darken3).Padding(5).AlignCenter().Text("Heads (obt/max)").Bold().FontColor(Colors.White).FontSize(9);
                        header.Cell().Background(Colors.Blue.Darken3).Padding(5).AlignCenter().Text("Total").Bold().FontColor(Colors.White).FontSize(9);
                        header.Cell().Background(Colors.Blue.Darken3).Padding(5).AlignCenter().Text("Grd").Bold().FontColor(Colors.White).FontSize(9);
                        header.Cell().Background(Colors.Blue.Darken3).Padding(5).AlignCenter().Text("GP").Bold().FontColor(Colors.White).FontSize(9);
                        header.Cell().Background(Colors.Blue.Darken3).Padding(5).AlignCenter().Text("C").Bold().FontColor(Colors.White).FontSize(9);
                        header.Cell().Background(Colors.Blue.Darken3).Padding(5).AlignCenter().Text("CG").Bold().FontColor(Colors.White).FontSize(9);
                    });

                    foreach (var sub in Model.Subjects)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(sub.SubjectCode).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignLeft().Text(sub.SubjectName).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(text =>
                        {
                            foreach (var head in sub.Heads)
                            {
                                text.Line($"{head.Head}: {head.Marks}{head.Grace}/{head.Max:0.##}").FontSize(8);
                            }
                        });
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text($"{sub.TotalObtained}/{sub.TotalMax}").FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(sub.Grade).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(sub.GradePoint.ToString()).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(sub.Credits.ToString()).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(sub.EarnedGradePoints.ToString()).FontSize(9);
                    }
                });

                column.Item().PaddingTop(15).Background(Colors.Blue.Lighten5).Border(1).BorderColor(Colors.Blue.Lighten3).Padding(8).Row(row =>
                {
                    row.RelativeItem().AlignCenter().Text(text => { text.Span("Total Marks\n").Bold().FontSize(8).FontColor(Colors.Blue.Darken3); text.Span($"{Model.TotalObtained} / {Model.TotalMax}").FontSize(11).Bold(); });
                    row.RelativeItem().AlignCenter().Text(text => { text.Span("Credits Earned\n").Bold().FontSize(8).FontColor(Colors.Blue.Darken3); text.Span($"{Model.CreditsEarned} / {Model.TotalCredits}").FontSize(11).Bold(); });
                    row.RelativeItem().AlignCenter().Text(text => { text.Span("SGPI\n").Bold().FontSize(8).FontColor(Colors.Blue.Darken3); text.Span($"{Model.SGPI:F2}").FontSize(11).Bold(); });
                    row.RelativeItem().AlignCenter().Text(text => { text.Span("CGPI\n").Bold().FontSize(8).FontColor(Colors.Blue.Darken3); text.Span(Model.CGPI.HasValue ? $"{Model.CGPI:F2}" : "-").FontSize(11).Bold(); });
                    row.RelativeItem().AlignCenter().Text(text => { text.Span("Remark\n").Bold().FontSize(8).FontColor(Colors.Blue.Darken3); text.Span(Model.Remark).FontSize(11).Bold().FontColor(Model.Remark.Equals("Pass", StringComparison.OrdinalIgnoreCase) || Model.Remark.Equals("SUCCESSFUL", StringComparison.OrdinalIgnoreCase) ? Colors.Green.Darken2 : Colors.Red.Darken2); });
                });

                if (Model.PastSemesters != null && Model.PastSemesters.Any())
                {
                    column.Item().PaddingTop(20).Text("Semester History").FontSize(11).Bold().FontColor(Colors.Blue.Darken3);
                    column.Item().PaddingTop(5).Table(table => 
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("Semester").Bold().FontSize(9);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("Credits").Bold().FontSize(9);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("Earned Grade Points (CG)").Bold().FontSize(9);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("SGPI").Bold().FontSize(9);
                        });

                        foreach (var pastSem in Model.PastSemesters)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(pastSem.SemesterName).FontSize(9);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(pastSem.Credits.ToString()).FontSize(9);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(pastSem.EarnedGradePoints.ToString()).FontSize(9);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(pastSem.SGPI.ToString("F2")).FontSize(9);
                        }
                    });
                }
            });
        }

        void ComposeFooter(IContainer container)
        {
            container.PaddingTop(30).Row(row =>
            {
                row.RelativeItem().AlignLeft().Text($"Date: {Model.ResultDate:dd/MM/yyyy}").FontSize(10);
                row.RelativeItem().AlignRight().Text("Principal / Exam Controller").FontSize(10).Bold();
            });
        }
    }
}
