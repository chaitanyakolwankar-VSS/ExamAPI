using ExamAPI.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ExamAPI.Services.Report.Documents
{
    public class GazetteDocument : IDocument
    {
        public GazetteReportDto Model { get; }
        public GazetteRequestDto Request { get; }

        public GazetteDocument(GazetteReportDto model, GazetteRequestDto request)
        {
            Model = model;
            Request = request;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container
                .Page(page =>
                {
                    page.Size(PageSizes.A3.Landscape()); // Wide page for multiple columns
                    page.Margin(20, Unit.Point);
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
                column.Item().AlignCenter().Text(Model.CollegeName).FontSize(16).SemiBold(); // Dynamically fetched
                column.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().AlignLeft().Text($"Program Name: {Model.ProgramName}").FontSize(12).Bold();
                    row.RelativeItem().AlignRight().Text($"Result Date : {Model.ResultDate:dd/MM/yyyy}").FontSize(12).Bold();
                });
                column.Item().PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().AlignLeft().Text($"{Model.Semester}").FontSize(12).Bold();
                    row.RelativeItem().AlignRight().Text($"Exam: {Model.ExamName}").FontSize(12).Bold();
                });
                column.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Black);
            });
        }

        void ComposeContent(IContainer container)
        {
            var studentChunks = Model.Students.Chunk(Request.StudentsPerPage).ToList();

            container.PaddingVertical(10).Column(column =>
            {
                for (int chunkIndex = 0; chunkIndex < studentChunks.Count; chunkIndex++)
                {
                    var chunk = studentChunks[chunkIndex];

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3); // SeatNo / PRN / Name
                            for (int i = 0; i < Request.SubjectsPerRow; i++)
                            {
                                columns.RelativeColumn(2); // Sub
                            }
                            columns.RelativeColumn(1); // Obt/Tot
                            columns.RelativeColumn(1); // CG / CE
                            columns.RelativeColumn(1); // SGPA
                            if (Model.ShowCgpi)
                            {
                                columns.RelativeColumn(1); // CGPI
                            }
                            columns.RelativeColumn(1); // Remark
                        });

                        table.Header(header =>
                        {
                            header.Cell().Border(1).Padding(2).AlignCenter().Text("Seat No / PRN /\nName of Student / Stud ID").Bold();
                            // Dynamically render subject headers
                            for(int i = 0; i < Request.SubjectsPerRow; i++) 
                            {
                                header.Cell().Border(1).Padding(2).AlignCenter().Text($"SubCode\nHead types\nMin/Max").Bold();
                            }
                            header.Cell().Border(1).Padding(2).AlignCenter().Text("Obt/Tot").Bold();
                            header.Cell().Border(1).Padding(2).AlignCenter().Text("CG\nCE").Bold();
                            header.Cell().Border(1).Padding(2).AlignCenter().Text("SGPA").Bold();
                            if(Model.ShowCgpi)
                            {
                                header.Cell().Border(1).Padding(2).AlignCenter().Text("CGPI").Bold();
                            }
                            header.Cell().Border(1).Padding(2).AlignCenter().Text("Remark").Bold();
                        });

                        foreach (var student in chunk)
                        {
                            table.Cell().Border(1).Padding(2).Text($"{student.SeatNo} /\n{student.PRN} /\n{student.StudentName} /\n{student.StudentId}");
                            
                            // Subjects
                            int subCount = 0;
                            foreach(var sub in student.Subjects)
                            {
                                table.Cell().Border(1).Padding(2).AlignCenter().Text(text =>
                                {
                                    text.Line($"{sub.SubjectCode}").Bold();
                                    text.Line($"{sub.Head1Type} / {sub.Head2Type}");
                                    text.Line($"{sub.Head1Max} / {sub.Head2Max}");
                                    text.Line($"{sub.Head1Marks}{sub.Head1Grace} / {sub.Head2Marks}");
                                    text.Line($"C: {sub.Credits} G: {sub.Grade} GP: {sub.GradePoint} CG: {sub.EarnedGradePoints}");
                                });
                                subCount++;
                            }
                            // Fill remaining subject columns if less than config
                            for(int i = subCount; i < Request.SubjectsPerRow; i++)
                            {
                                table.Cell().Border(1).Padding(2).Text("");
                            }

                            table.Cell().Border(1).Padding(2).AlignCenter().Text($"{student.TotalObtained}/{student.TotalMax}").Bold();
                            table.Cell().Border(1).Padding(2).AlignCenter().Text($"{student.SGPI} / {student.CreditsEarned}").Bold();
                            table.Cell().Border(1).Padding(2).AlignCenter().Text($"{student.SGPI}").Bold();
                            if(Model.ShowCgpi)
                            {
                                table.Cell().Border(1).Padding(2).AlignCenter().Text($"{student.CGPI}").Bold();
                            }
                            table.Cell().Border(1).Padding(2).AlignCenter().Text($"{student.Remark}").Bold();
                        }
                    });

                    if (chunkIndex < studentChunks.Count - 1)
                    {
                        column.Item().PageBreak();
                    }
                }
            });
        }

        void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(x =>
            {
                x.Span("Page ");
                x.CurrentPageNumber();
                x.Span(" of ");
                x.TotalPages();
            });
        }
    }
}
