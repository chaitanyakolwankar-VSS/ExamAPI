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
                column.Item().AlignCenter().Text("UNIVERSITY OF MUMBAI").FontSize(14).Bold();
                column.Item().AlignCenter().Text("LOKMANYA TILAK COLLEGE OF ENGINEERING").FontSize(16).Bold();
                column.Item().AlignCenter().Text("STATEMENT OF MARKS").FontSize(12).SemiBold().Underline();
                
                column.Item().PaddingTop(15).Row(row =>
                {
                    row.RelativeItem().Column(col => 
                    {
                        col.Item().Text(text => { text.Span("Name: ").Bold(); text.Span(Model.StudentName); });
                        col.Item().Text(text => { text.Span("Program: ").Bold(); text.Span(Model.ProgramName); });
                    });
                    
                    row.RelativeItem().Column(col => 
                    {
                        col.Item().AlignRight().Text(text => { text.Span("Seat No: ").Bold(); text.Span(Model.SeatNo); });
                        col.Item().AlignRight().Text(text => { text.Span("PRN: ").Bold(); text.Span(Model.PRN); });
                    });
                });
                
                column.Item().PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().Text(text => { text.Span("Examination: ").Bold(); text.Span(Model.ExamName); });
                    row.RelativeItem().AlignRight().Text(text => { text.Span("Semester: ").Bold(); text.Span(Model.Semester); });
                });
                
                column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Black);
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
                        columns.RelativeColumn(2);  // Configured heads
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
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("Heads (obt/max)").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("Total").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("Grd").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("GP").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("C").Bold();
                        header.Cell().Border(1).Padding(2).AlignCenter().Text("CG").Bold();
                    });

                    foreach (var sub in Model.Subjects)
                    {
                        table.Cell().Border(1).Padding(2).AlignCenter().Text(sub.SubjectCode);
                        table.Cell().Border(1).Padding(2).AlignLeft().Text(sub.SubjectName);
                        table.Cell().Border(1).Padding(2).AlignCenter().Text(text =>
                        {
                            foreach (var head in sub.Heads)
                            {
                                text.Line($"{head.Head}: {head.Marks}{head.Grace}/{head.Max:0.##}");
                            }
                        });
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

                    table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("Total Marks:\n").Bold(); text.Span($"{Model.TotalObtained} / {Model.TotalMax}"); });
                    table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("Credits:\n").Bold(); text.Span($"{Model.CreditsEarned} / {Model.TotalCredits}"); });
                    table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("SGPI:\n").Bold(); text.Span($"{Model.SGPI}"); });
                    
                    if (Model.CGPI.HasValue)
                    {
                        table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("CGPI:\n").Bold(); text.Span($"{Model.CGPI}"); });
                    }
                    else
                    {
                        table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("CGPI:\n").Bold(); text.Span("-"); });
                    }

                    table.Cell().Border(1).Padding(4).AlignCenter().Text(text => { text.Span("Remark:\n").Bold(); text.Span(Model.Remark); });
                });

                if (Model.PastSemesters != null && Model.PastSemesters.Any())
                {
                    column.Item().PaddingTop(20).Text("Semester History").FontSize(11).Bold().Underline();
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
                            header.Cell().Border(1).Padding(2).AlignCenter().Text("Semester").Bold();
                            header.Cell().Border(1).Padding(2).AlignCenter().Text("Credits").Bold();
                            header.Cell().Border(1).Padding(2).AlignCenter().Text("Earned Grade Points (CG)").Bold();
                            header.Cell().Border(1).Padding(2).AlignCenter().Text("SGPI").Bold();
                        });

                        foreach (var pastSem in Model.PastSemesters)
                        {
                            table.Cell().Border(1).Padding(2).AlignCenter().Text(pastSem.SemesterName);
                            table.Cell().Border(1).Padding(2).AlignCenter().Text(pastSem.Credits.ToString());
                            table.Cell().Border(1).Padding(2).AlignCenter().Text(pastSem.EarnedGradePoints.ToString());
                            table.Cell().Border(1).Padding(2).AlignCenter().Text(pastSem.SGPI.ToString());
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
