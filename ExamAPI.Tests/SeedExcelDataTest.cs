using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ExamAPI.Models;
using ExamAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Moq;
using OfficeOpenXml;
using System.Collections.Generic;

namespace ExamAPI.Tests
{
    public class SeedExcelDataTest
    {
        [Fact]
        public async Task BulkSeedFromExcel()
        {
            // 1. Database Connection Options (Point to the real database)
            var connectionString = "Data Source=100.121.68.85,11433;Initial Catalog=DBExamAPI;User ID=sa;Password=passwd@12;TrustServerCertificate=True;MultipleActiveResultSets=True";
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
            using (var context = new ApplicationDbContext(options, mockHttpContextAccessor.Object))
            {
                // Set EPPlus License
                ExcelPackage.License.SetNonCommercialPersonal("ReactApi Project");

                // File Path
                var excelPath = @"D:\Projects\ReactApi\Computer Sem - VI Regular May 2025 - Copy (1).xlsx";
                Assert.True(File.Exists(excelPath), "Excel file not found!");

                using (var transaction = await context.Database.BeginTransactionAsync())
                {
                    try
                    {
                        // 2. Clear out existing transactional tables
                        await context.Database.ExecuteSqlRawAsync("DELETE FROM StudentMarks");
                        await context.Database.ExecuteSqlRawAsync("DELETE FROM MarksMaster");
                        await context.Database.ExecuteSqlRawAsync("DELETE FROM StudentEligibility");
                        await context.Database.ExecuteSqlRawAsync("DELETE FROM StudentMaster");
                        await context.Database.ExecuteSqlRawAsync("DELETE FROM SubjectCredits");
                        await context.Database.ExecuteSqlRawAsync("DELETE FROM SubjectCreditMaster");
                        await context.Database.ExecuteSqlRawAsync("DELETE FROM SubjectMaster");
                        await context.Database.ExecuteSqlRawAsync("DELETE FROM ExamMaster");

                        // 3. Insert ExamMaster
                        var collegeId = Guid.Parse("103EBF99-FEB0-43BC-A312-56FE85D3BCC6");
                        var courseId = Guid.Parse("78636067-D554-4F7F-9311-227D300498D9"); // Computer Science
                        var ayId = Guid.Parse("510CB442-962A-4018-8D34-7A5D69FD9060"); // 2024-2025
                        var examId = Guid.Parse("BC017F31-95FD-4EF5-B113-08DE5D670F0B"); // TE Sem-VI Exam

                        var exam = new ExamMaster
                        {
                            ExamId = examId,
                            Name = "T.E. Sem-VI Regular Exam May 2025",
                            ExamType = "Regular",
                            Semester = "Sem-6",
                            CourseId = courseId,
                            AcademicYearAYID = ayId,
                            IsActive = true,
                            IsLocked = false,
                            CreatedAt = DateTime.UtcNow,
                            IsDeleted = false
                        };
                        context.Exams.Add(exam);

                        // 4. Define CScheme/NEP subjects mapping from Excel Columns
                        var subjectsConfig = new[]
                        {
                            new { Code = "CSC601", Name = "System Programming & Compiler Construction", TotalCredits = "3", Heads = new[] { new { Name = "Theory", OutOf = 80, Pass = 32, Col = 3, Type = "TH" }, new { Name = "TermWork", OutOf = 20, Pass = 8, Col = 4, Type = "TW" } } },
                            new { Code = "CSC602", Name = "Cryptography & System Security", TotalCredits = "3", Heads = new[] { new { Name = "Theory", OutOf = 80, Pass = 32, Col = 6, Type = "TH" }, new { Name = "TermWork", OutOf = 20, Pass = 8, Col = 7, Type = "TW" } } },
                            new { Code = "CSC603", Name = "Mobile Computing", TotalCredits = "3", Heads = new[] { new { Name = "Theory", OutOf = 80, Pass = 32, Col = 9, Type = "TH" }, new { Name = "TermWork", OutOf = 20, Pass = 8, Col = 10, Type = "TW" } } },
                            new { Code = "CSC604", Name = "Artificial Intelligence", TotalCredits = "3", Heads = new[] { new { Name = "Theory", OutOf = 80, Pass = 32, Col = 12, Type = "TH" }, new { Name = "TermWork", OutOf = 20, Pass = 8, Col = 13, Type = "TW" } } },
                            new { Code = "CSDLO6011", Name = "Internet of Things", TotalCredits = "3", Heads = new[] { new { Name = "Theory", OutOf = 80, Pass = 32, Col = 15, Type = "TH" }, new { Name = "TermWork", OutOf = 20, Pass = 8, Col = 16, Type = "TW" } } },
                            new { Code = "CSL601", Name = "System Programming & Compiler Construction Lab", TotalCredits = "1", Heads = new[] { new { Name = "PR OR", OutOf = 25, Pass = 10, Col = 18, Type = "PR" }, new { Name = "TW", OutOf = 25, Pass = 10, Col = 19, Type = "TW" } } },
                            new { Code = "CSL602", Name = "Cryptography & System Security Lab", TotalCredits = "1", Heads = new[] { new { Name = "TW", OutOf = 25, Pass = 10, Col = 21, Type = "TW" } } },
                            new { Code = "CSL603", Name = "Mobile Computing Lab", TotalCredits = "1", Heads = new[] { new { Name = "TW", OutOf = 25, Pass = 10, Col = 22, Type = "TW" } } },
                            new { Code = "CSL604", Name = "Artificial Intelligence Lab", TotalCredits = "1", Heads = new[] { new { Name = "PR OR", OutOf = 25, Pass = 10, Col = 23, Type = "PR" }, new { Name = "TW", OutOf = 25, Pass = 10, Col = 24, Type = "TW" } } },
                            new { Code = "CSL605", Name = "Skill base Lab Course - Cloud Computing", TotalCredits = "2", Heads = new[] { new { Name = "PR OR", OutOf = 25, Pass = 10, Col = 26, Type = "PR" }, new { Name = "TW", OutOf = 50, Pass = 20, Col = 27, Type = "TW" } } },
                            new { Code = "CSM601", Name = "Mini Project - 2B", TotalCredits = "2", Heads = new[] { new { Name = "PR OR", OutOf = 25, Pass = 10, Col = 29, Type = "PR" }, new { Name = "TW", OutOf = 25, Pass = 10, Col = 30, Type = "TW" } } }
                        };

                        // Map structure for easy lookups during student parsing
                        var dbSubjectMap = new Dictionary<string, (Guid SubjectId, Guid CreditsId, List<(string HeadName, int Col, int Passing, int OutOf)> Heads)>();

                        foreach (var sub in subjectsConfig)
                        {
                            var subjectId = Guid.NewGuid();
                            var subject = new SubjectMaster
                            {
                                SubjectId = subjectId,
                                SubjectCode = sub.Code,
                                Name = sub.Name,
                                SemId = "Sem-6",
                                SemName = "Semester VI",
                                Pattern = "NEP", // We are using NEP pattern
                                CourseId = courseId,
                                CreatedAt = DateTime.UtcNow,
                                IsDeleted = false
                            };
                            context.SubjectMasters.Add(subject);

                            var creditsId = Guid.NewGuid();
                            var creditMaster = new SubjectCreditMaster
                            {
                                CreditsId = creditsId,
                                TotalCredits = sub.TotalCredits,
                                AYID = ayId.ToString(),
                                SubjectId = subjectId,
                                CreatedAt = DateTime.UtcNow,
                                IsDeleted = false
                            };
                            context.SubjectCreditMasters.Add(creditMaster);

                            var headsList = new List<(string HeadName, int Col, int Passing, int OutOf)>();
                            foreach (var head in sub.Heads)
                            {
                                var creditHead = new SubjectCredits
                                {
                                    Id = Guid.NewGuid(),
                                    Head = head.Name,
                                    HeadType = head.Type,
                                    HeadOutOf = head.OutOf.ToString(),
                                    HeadPass = head.Pass.ToString(),
                                    CreditsId = creditsId,
                                    CreatedAt = DateTime.UtcNow,
                                    IsDeleted = false
                                };
                                context.SubjectCredits.Add(creditHead);
                                headsList.Add((head.Name, head.Col, head.Pass, head.OutOf));
                            }

                            dbSubjectMap[sub.Code] = (subjectId, creditsId, headsList);
                        }

                        // Save catalog first so FK validations pass
                        await context.SaveChangesAsync();

                        // 5. Open Excel package and parse students
                        using (var stream = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var package = new ExcelPackage(stream))
                        {
                            var ws = package.Workbook.Worksheets[0];
                            int rowCount = ws.Dimension.Rows;

                            int studentCount = 0;
                            // Student marks start at row 11. Each student block is 4 rows.
                            for (int row = 11; row <= rowCount; row += 4)
                            {
                                var seatNoRaw = ws.Cells[row, 1].Value?.ToString()?.Trim();
                                if (string.IsNullOrEmpty(seatNoRaw) || !long.TryParse(seatNoRaw, out var seatNoLong) || seatNoLong < 100000)
                                {
                                    // Row doesn't contain a valid seat number, skip
                                    continue;
                                }

                                var seatNo = seatNoRaw;
                                var studentNameRaw = ws.Cells[row + 1, 1].Value?.ToString()?.Trim() ?? $"Student_{seatNo}";

                                // Split FirstName / LastName
                                string firstName = studentNameRaw;
                                string lastName = "Test";
                                int spaceIdx = studentNameRaw.IndexOf(' ');
                                if (spaceIdx > 0)
                                {
                                    firstName = studentNameRaw.Substring(0, spaceIdx);
                                    lastName = studentNameRaw.Substring(spaceIdx + 1);
                                }

                                var stdMstId = Guid.NewGuid();
                                var student = new StudentMaster
                                {
                                    StdMstId = stdMstId,
                                    StudentId = seatNo,
                                    FirstName = firstName,
                                    LastName = lastName,
                                    CollegeId = collegeId,
                                    CreatedAt = DateTime.UtcNow,
                                    IsDeleted = false
                                };
                                context.StudentMasters.Add(student);

                                var eligibility = new StudentEligibility
                                {
                                    Id = Guid.NewGuid(),
                                    StdMstId = stdMstId,
                                    CourseId = courseId,
                                    AYID = ayId,
                                    StudentId = seatNo,
                                    SemesterId = "Sem-6",
                                    Pattern = "NEP",
                                    CreatedAt = DateTime.UtcNow,
                                    IsDeleted = false
                                };
                                context.StudentEligibilities.Add(eligibility);

                                var marksId = Guid.NewGuid();
                                var marksMaster = new MarksMaster
                                {
                                    MarksId = marksId,
                                    StudentID = seatNo,
                                    SeatNo = seatNo,
                                    SemesterId = "Sem-6",
                                    Pattern = "NEP",
                                    OverallRemark = "Pending",
                                    StdMstId = stdMstId,
                                    ExamId = examId,
                                    AcademicYearAYID = ayId,
                                    HMCheck = true,
                                    CreatedAt = DateTime.UtcNow,
                                    IsDeleted = false
                                };
                                context.MarksMasters.Add(marksMaster);

                                // Add marks for each subject head
                                foreach (var sub in subjectsConfig)
                                {
                                    var (subjId, credId, heads) = dbSubjectMap[sub.Code];
                                    foreach (var head in heads)
                                    {
                                        var markRaw = ws.Cells[row, head.Col].Value?.ToString()?.Trim();
                                        int? mark = null;
                                        string remark = null;

                                        if (!string.IsNullOrEmpty(markRaw))
                                        {
                                            if (string.Equals(markRaw, "ab", StringComparison.OrdinalIgnoreCase))
                                            {
                                                remark = "Ab";
                                            }
                                            else if (int.TryParse(markRaw, out int mValue))
                                            {
                                                mark = mValue;
                                                remark = (mValue >= head.Passing) ? "Successful" : "Unsuccessful";
                                            }
                                        }

                                        var studentMark = new StudentMarks
                                        {
                                            Id = Guid.NewGuid(),
                                            IsCarryForward = false,
                                            Head = head.HeadName,
                                            RawMarks = mark,
                                            Marks = mark,
                                            Remark = remark,
                                            MarksId = marksId,
                                            SubjectId = subjId,
                                            CreditsId = credId,
                                            CreatedAt = DateTime.UtcNow,
                                            IsDeleted = false
                                        };
                                        context.StudentMarks.Add(studentMark);
                                    }
                                }

                                studentCount++;
                            }

                            await context.SaveChangesAsync();
                            Console.WriteLine($"Seeded {studentCount} students successfully.");
                        }

                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        throw new Exception($"Bulk seed failed: {ex.Message}", ex);
                    }
                }
            }
        }
    }
}
