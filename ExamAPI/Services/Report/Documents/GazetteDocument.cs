using ExamAPI.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

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
                    page.Size(PageSizes.A4.Landscape()); // Match A4 Landscape
                    page.Margin(12, Unit.Point); // Minimal margins to maximize data area
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(8).FontFamily(Fonts.Arial)); // Size 8 for A4 Landscape

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeContent);
                    page.Footer().Element(ComposeFooter);
                });
        }

        void ComposeHeader(IContainer container)
        {
            container.Column(column =>
            {
                // Spacious header, allows room for a potential logo on the left/center
                column.Item().Row(row => 
                {
                    row.RelativeItem().AlignCenter().Text(Model.CollegeName).FontSize(14).SemiBold();
                });
                
                column.Item().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().AlignLeft().Text($"Program Name: {Model.ProgramName}").FontSize(10).Bold();
                    row.RelativeItem().AlignRight().Text($"Result Date : {Model.ResultDate:dd/MM/yyyy}").FontSize(10).Bold();
                });
                
                column.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().AlignLeft().Text($"{Model.Semester}").FontSize(10).Bold();
                    row.RelativeItem().AlignRight().Text($"Exam: {Model.ExamName}").FontSize(10).Bold();
                });
                
                column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Black);
            });
        }

        void ComposeContent(IContainer container)
        {
            var studentsPerPage = Math.Clamp(Request.StudentsPerPage, 1, 4);
            var subjectsPerRow = Math.Clamp(Request.SubjectsPerRow, 1, 6);
            var studentChunks = Model.Students.Chunk(studentsPerPage).ToList();

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
                            for (int i = 0; i < subjectsPerRow; i++)
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
                            header.Cell().Border(1).Padding(2).AlignCenter().Text("Student Details").Bold();
                            // Dynamically render subject headers
                            for(int i = 0; i < subjectsPerRow; i++) 
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
                            var subjectRows = student.Subjects.Chunk(subjectsPerRow).ToList();
                            if (subjectRows.Count == 0)
                            {
                                subjectRows.Add(Array.Empty<SubjectMarksDto>());
                            }

                            uint rowSpan = (uint)subjectRows.Count;

                            for (var rowIndex = 0; rowIndex < subjectRows.Count; rowIndex++)
                            {
                                var subjectRow = subjectRows[rowIndex];
                                var isFirstRow = rowIndex == 0;
                                var hasFailure = student.Subjects.Any(subject => string.Equals(subject.Grade, "F", StringComparison.OrdinalIgnoreCase));
                                var displayRemark = hasFailure && Request.NoRleForFail && string.Equals(student.Remark, "RLE", StringComparison.OrdinalIgnoreCase)
                                    ? "Fail"
                                    : student.Remark;
                                var sgpi = hasFailure && !Request.SgpiForFail ? "--" : FormatNumber(student.SGPI);
                                var cgpi = hasFailure && !Request.CgpiForFail ? "--" : FormatNumber(student.CGPI ?? 0);

                                if (isFirstRow)
                                {
                                    table.Cell().RowSpan(rowSpan).Border(1).Padding(2).AlignMiddle().Column(c => 
                                    {
                                        c.Item().Text(student.StudentName).Bold();
                                        c.Item().Text($"Seat No: {student.SeatNo}").SemiBold();
                                        c.Item().Text($"PRN: {student.PRN}");
                                    });
                                }

                                foreach (var sub in subjectRow)
                                {
                                    table.Cell().Border(1).Padding(2).AlignCenter().Text(text =>
                                    {
                                        text.Line($"{sub.SubjectCode}").Bold();
                                        foreach (var head in sub.Heads)
                                        {
                                            text.Line($"{head.Head}: {head.Marks}{head.Grace}/{FormatNumber(head.Max)}");
                                        }
                                        text.Line($"C: {sub.Credits} G: {sub.Grade} GP: {sub.GradePoint} CG: {sub.EarnedGradePoints}");
                                    });
                                }

                                for (var subjectIndex = subjectRow.Length; subjectIndex < subjectsPerRow; subjectIndex++)
                                {
                                    table.Cell().Border(1).Padding(2).Text(string.Empty);
                                }

                                if (isFirstRow)
                                {
                                    table.Cell().RowSpan(rowSpan).Border(1).Padding(2).AlignCenter().AlignMiddle().Text($"{FormatNumber(student.TotalObtained)}/{FormatNumber(student.TotalMax)}").Bold();
                                    table.Cell().RowSpan(rowSpan).Border(1).Padding(2).AlignCenter().AlignMiddle().Text($"{FormatNumber(student.CumulativeGrade ?? 0)} / {FormatNumber(student.CreditsEarned)}").Bold();
                                    table.Cell().RowSpan(rowSpan).Border(1).Padding(2).AlignCenter().AlignMiddle().Text(sgpi).Bold();
                                    if(Model.ShowCgpi)
                                    {
                                        table.Cell().RowSpan(rowSpan).Border(1).Padding(2).AlignCenter().AlignMiddle().Text(cgpi).Bold();
                                    }
                                    table.Cell().RowSpan(rowSpan).Border(1).Padding(2).AlignCenter().AlignMiddle().Text(displayRemark).Bold();
                                }
                            }
                        }
                    });

                    if (chunkIndex < studentChunks.Count - 1)
                    {
                        column.Item().PageBreak();
                    }
                }
            });
        }

        private string FormatNumber(double value)
        {
            return Request.RoundNumber
                ? Math.Round(value, MidpointRounding.AwayFromZero).ToString()
                : value.ToString("0.##");
        }

        void ComposeFooter(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Black);
                
                // Legends section - Row styled to fit every page
                column.Item().PaddingTop(5).Row(row => 
                {
                    row.RelativeItem().Column(left => 
                    {
                        var distinctSubjects = Model.Students
                            .SelectMany(s => s.Subjects)
                            .GroupBy(s => s.SubjectCode)
                            .Select(g => g.First())
                            .OrderBy(s => s.SubjectCode)
                            .ToList();
                            
                        var subjectText = string.Join("  |  ", distinctSubjects.Select(s => $"{s.SubjectCode}: {s.SubjectName ?? "-"}"));
                        left.Item().Text(text => 
                        {
                            text.Span("Subjects: ").Bold().FontSize(7);
                            text.Span(subjectText).FontSize(7);
                        });
                    });
                });
                
                column.Item().PaddingTop(3).Row(row => 
                {
                    row.RelativeItem().Column(col => 
                    {
                        var abbrText = "C: Credits  |  G: Grade  |  GP: Grade Point  |  CG: Credits * Grade Point  |  CE: Credits Earned  |  SGPA: Semester Grade Point Average  |  CGPI: Cumulative Grade Point Index  |  --: Not Applicable  |  F: Fail  |  AB: Absent";
                        col.Item().Text(text => 
                        {
                            text.Span("Abbreviations: ").Bold().FontSize(7);
                            text.Span(abbrText).FontSize(7);
                        });
                    });
                });

                column.Item().PaddingTop(5).AlignCenter().Text(x =>
                {
                    x.Span("Page ").FontSize(10);
                    x.CurrentPageNumber().FontSize(10);
                    x.Span(" of ").FontSize(10);
                    x.TotalPages().FontSize(10);
                });
            });
        }
    }
}
