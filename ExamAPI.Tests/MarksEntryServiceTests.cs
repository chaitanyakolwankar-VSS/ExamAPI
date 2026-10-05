using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ExamAPI.Services.MarksEntry;
using ExamAPI.Models;
using ExamAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Moq;
using ClosedXML.Excel;
using ExamAPI.DTOs;
using ExamAPI.Services.Tenancy;

namespace ExamAPI.Tests
{
    public class MarksEntryServiceTests
    {
        private const string Semester = "Sem-6";
        private const string Pattern = "NEP";

        private readonly MarksEntryService _service;
        private readonly ApplicationDbContext _context;

        /// <summary>
        /// The tenant the context filters every query to. Set it before seeding, or the global
        /// college filter hides the rows the test just added.
        /// </summary>
        private Guid? _currentCollegeId;

        private readonly Guid _collegeId = Guid.NewGuid();
        private readonly Guid _courseId = Guid.NewGuid();
        private readonly Guid _examId = Guid.NewGuid();

        public MarksEntryServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
            var mockCurrentUser = new Mock<ICurrentUser>();
            mockCurrentUser.SetupGet(user => user.CollegeId).Returns(() => _currentCollegeId);

            _context = new ApplicationDbContext(options, mockHttpContextAccessor.Object, mockCurrentUser.Object);

            _service = new MarksEntryService(_context);

            _currentCollegeId = _collegeId;
            _context.Exams.Add(new ExamMaster
            {
                ExamId = _examId, CollegeId = _collegeId, CourseId = _courseId, Name = "Regular", ExamType = "Regular"
            });
            _context.SaveChanges();
        }

        // ------------------------------------------------------------------ import / save

        [Fact]
        public async Task ImportMarksExcelAsync_stores_raw_marks_only_and_leaves_resolution_to_result_processing()
        {
            var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 100, 40));
            var studentMarks = AddStudent("ST001", subject, 30).Single();
            AddLimit(subject, "H1", 5); // configured -- but import must not apply it
            await _context.SaveChangesAsync();

            var result = await _service.ImportMarksExcelAsync(_examId, subject.SubjectId, BuildTemplate(studentMarks), _collegeId);

            Assert.True(result.Success, result.Message);
            var updated = await _context.StudentMarks.FindAsync(studentMarks.Id);
            Assert.Equal(35, updated!.RawMarks);
            Assert.Equal(35, updated.Marks);
            Assert.Null(updated.Resolution);
            Assert.Null(updated.Grace);
        }

        [Fact]
        public async Task SaveMarksAsync_stores_raw_marks_only_and_clears_a_stale_derived_bump()
        {
            var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 100, 40));
            var mark = AddStudent("ST001", subject, 35).Single();
            mark.Marks = 40; mark.Resolution = 5; mark.Grace = "^"; // left over from an earlier processing run
            AddLimit(subject, "H1", 5);
            await _context.SaveChangesAsync();

            var result = await _service.SaveMarksAsync(SaveRequest((mark.Id, "36")), _collegeId);

            Assert.True(result.Success, result.Message);
            Assert.Equal(36, mark.RawMarks);
            Assert.Equal(36, mark.Marks);
            Assert.Null(mark.Resolution);
            Assert.Null(mark.Grace);
        }

        [Fact]
        public async Task SaveMarksAsync_does_not_modify_a_carried_forward_head_BUG_16()
        {
            var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 100, 40), ("H2", 100, 40));
            var marks = AddStudent("ST001", subject, 55, 50);
            var carried = marks.Single(m => m.Head == "H1");
            var fresh = marks.Single(m => m.Head == "H2");
            carried.IsCarryForward = true;
            carried.Resolution = 2;
            carried.Grace = "^";
            await _context.SaveChangesAsync();

            var result = await _service.SaveMarksAsync(SaveRequest((carried.Id, "10"), (fresh.Id, "60")), _collegeId);

            Assert.True(result.Success, result.Message);
            Assert.Contains("1 carried-forward", result.Message);
            Assert.Equal(55, carried.RawMarks);
            Assert.Equal(2, carried.Resolution);
            Assert.Equal("^", carried.Grace);
            Assert.Equal(60, fresh.RawMarks);
        }

        [Fact]
        public async Task SaveMarksAsync_reports_an_error_when_every_edited_head_is_carried_forward()
        {
            var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 100, 40));
            var carried = AddStudent("ST001", subject, 55).Single();
            carried.IsCarryForward = true;
            await _context.SaveChangesAsync();

            var result = await _service.SaveMarksAsync(SaveRequest((carried.Id, "10")), _collegeId);

            Assert.False(result.Success);
            Assert.Contains("carried forward", result.Message);
            Assert.Equal(55, carried.RawMarks);
        }

        [Fact]
        public async Task Exported_template_imports_back_with_typed_numbers_and_Ab()
        {
            var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 100, 40));
            var first = AddStudent("ST001", subject).Single();
            var second = AddStudent("ST002", subject).Single();
            await _context.SaveChangesAsync();

            var template = await _service.ExportTemplateExcelAsync(new MarksEntryFilterRequest
            {
                BranchId = _courseId, SemId = Semester, Pattern = Pattern, ExamId = _examId, SubjectId = subject.SubjectId
            }, _collegeId);

            // Staff fill it in Excel: a typed number is a numeric cell, "Ab" is text.
            byte[] filled;
            using (var workbook = new XLWorkbook(new MemoryStream(template)))
            {
                var ws = workbook.Worksheet(1);
                Assert.EndsWith("_ID", ws.Cell(6, 5).GetText());
                Assert.True(ws.Column(5).IsHidden);
                Assert.Single(ws.DataValidations);
                var rowOf = (Guid id) => Enumerable.Range(7, 2).Single(r => ws.Cell(r, 5).GetText() == id.ToString());
                ws.Cell(rowOf(first.Id), 4).Value = 42;
                ws.Cell(rowOf(second.Id), 4).Value = "Ab";
                filled = ExamAPI.Services.Report.ExcelStyles.ToBytes(workbook);
            }

            var result = await _service.ImportMarksExcelAsync(_examId, subject.SubjectId, filled, _collegeId);

            Assert.True(result.Success, result.Message);
            Assert.Equal(42, first.RawMarks);
            Assert.True(second.IsAbsent);
        }

        [Fact]
        public async Task ImportMarksExcelAsync_skips_carried_forward_heads_and_reports_how_many_BUG_16()
        {
            var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 100, 40));
            var carried = AddStudent("ST001", subject, 55).Single();
            var fresh = AddStudent("ST002", subject, 20).Single();
            carried.IsCarryForward = true;
            await _context.SaveChangesAsync();

            var result = await _service.ImportMarksExcelAsync(_examId, subject.SubjectId, BuildTemplate(carried, fresh), _collegeId);

            Assert.True(result.Success, result.Message);
            Assert.Contains("1 carried-forward", result.Message);
            Assert.Equal(55, carried.RawMarks);
            Assert.Equal(35, fresh.RawMarks);
        }

        // ------------------------------------------------------------------ resolution config: read

        [Fact]
        public async Task GetResolutionConfigAsync_lists_subjects_heads_limits_and_a_raw_marks_preview()
        {
            var headWise = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
            var combined = AddSubject(PassingStrategies.Combined, 40, ("H1", 50, 20), ("H2", 50, 20));
            AddStudent("ST001", headWise, 17, 30);   // H1 short by 3
            AddStudent("ST002", headWise, 10, 30);   // H1 short by 10
            AddStudent("ST003", headWise, 25, 25);   // passes
            AddStudent("ST004", combined, 20, 17);   // total 37, short by 3
            AddStudent("ST005", combined, 10, 10);   // total 20, short by 20
            var applied = AddStudent("ST006", combined, 30, 30);
            applied[0].Resolution = 2;               // a bump left by the last processing run
            AddLimit(headWise, "H1", 5);
            AddLimit(combined, "H2", 4);
            AddLimit(combined, "H1", 0);
            await _context.SaveChangesAsync();

            var response = await _service.GetResolutionConfigAsync(Config(), _collegeId);

            Assert.True(response.Success, response.Message);
            var data = response.Data!;
            Assert.Equal(_examId, data.ExamId);
            Assert.Equal(2, data.Subjects.Count);

            var hw = data.Subjects.Single(s => s.SubjectId == headWise.SubjectId);
            Assert.Equal(PassingStrategies.HeadWise, hw.PassingStrategy);
            Assert.Equal(3, hw.StudentCount);
            Assert.Equal(2, hw.FailingCount);
            Assert.Equal(1, hw.WithinLimitCount); // only ST001 is within the limit of 5
            Assert.Equal(0, hw.AppliedCount);
            var hwH1 = hw.Heads.Single(h => h.Head == "H1");
            Assert.Equal(("ESE", 50, 20, 5), (hwH1.HeadType, hwH1.OutOf, hwH1.Passing, hwH1.Limit));
            Assert.Equal(new[] { 3, 10 }, hwH1.Deficits);
            Assert.Equal(0, hw.Heads.Single(h => h.Head == "H2").Limit);

            var cb = data.Subjects.Single(s => s.SubjectId == combined.SubjectId);
            Assert.Equal(40, cb.PassPercentage);
            Assert.Equal(40, cb.RequiredToPass);
            Assert.Equal(combined.Heads.Single(h => h.Head == "H2").Id, cb.SelectedHeadSubjectCreditId);
            Assert.Equal(3, cb.StudentCount);
            Assert.Equal(2, cb.FailingCount);
            Assert.Equal(1, cb.WithinLimitCount);
            Assert.Equal(1, cb.AppliedCount);
            Assert.Equal(new[] { 3, 20 }, cb.Deficits);
        }

        [Fact]
        public async Task GetResolutionConfigAsync_shows_an_explicit_zero_and_ignores_other_colleges()
        {
            var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20));
            AddStudent("ST001", subject, 10);
            AddLimit(subject, "H1", 0);
            await _context.SaveChangesAsync();

            var mine = await _service.GetResolutionConfigAsync(Config(), _collegeId);
            var theirs = await _service.GetResolutionConfigAsync(Config(), Guid.NewGuid());

            Assert.Equal(0, mine.Data!.Subjects.Single().Heads.Single().Limit);
            Assert.Empty(theirs.Data!.Subjects);
        }

        // ------------------------------------------------------------------ resolution config: save

        [Fact]
        public async Task SaveResolutionConfigAsync_creates_then_updates_one_row_per_head_and_round_trips_zero()
        {
            var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20), ("H2", 50, 20));
            AddStudent("ST001", subject, 10, 10);
            await _context.SaveChangesAsync();
            var h1 = subject.Heads.Single(h => h.Head == "H1").Id;
            var h2 = subject.Heads.Single(h => h.Head == "H2").Id;

            var first = await _service.SaveResolutionConfigAsync(Save((h1, 250m), (h2, 0m)), _collegeId);
            var second = await _service.SaveResolutionConfigAsync(Save((h1, 7m), (h2, null)), _collegeId);

            Assert.True(first.Success, first.Message);
            Assert.Contains("Applies on next Process Results", first.Message);
            Assert.True(second.Success, second.Message);
            var rows = _context.Resolution.Where(r => r.ExamID == _examId).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(7, rows.Single(r => r.SubjectCreditID == h1).Resolution);
            Assert.Equal(0, rows.Single(r => r.SubjectCreditID == h2).Resolution);
            Assert.All(rows, r => Assert.Equal(_collegeId, r.CollegeId));

            var read = await _service.GetResolutionConfigAsync(Config(), _collegeId);
            Assert.Equal(7, read.Data!.Subjects.Single().Heads.Single(h => h.Head == "H1").Limit);
        }

        [Fact]
        public async Task SaveResolutionConfigAsync_rejects_negative_and_fractional_limits_without_writing()
        {
            var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20));
            AddStudent("ST001", subject, 10);
            await _context.SaveChangesAsync();
            var h1 = subject.Heads.Single().Id;

            var negative = await _service.SaveResolutionConfigAsync(Save((h1, -1m)), _collegeId);
            var fractional = await _service.SaveResolutionConfigAsync(Save((h1, 2.5m)), _collegeId);

            Assert.False(negative.Success);
            Assert.Contains("negative", negative.Message);
            Assert.False(fractional.Success);
            Assert.Contains("whole number", fractional.Message);
            Assert.Empty(_context.Resolution);
        }

        [Fact]
        public async Task SaveResolutionConfigAsync_allows_only_one_head_per_combined_subject()
        {
            var combined = AddSubject(PassingStrategies.Combined, 40, ("H1", 50, 20), ("H2", 50, 20));
            AddStudent("ST001", combined, 10, 10);
            await _context.SaveChangesAsync();
            var h1 = combined.Heads.Single(h => h.Head == "H1").Id;
            var h2 = combined.Heads.Single(h => h.Head == "H2").Id;

            var both = await _service.SaveResolutionConfigAsync(Save((h1, 3m), (h2, 4m)), _collegeId);
            Assert.False(both.Success);
            Assert.Contains("one head only", both.Message);
            Assert.Empty(_context.Resolution);

            // Moving the limit to the other head: the old head is zeroed in the same request.
            Assert.True((await _service.SaveResolutionConfigAsync(Save((h1, 3m), (h2, 0m)), _collegeId)).Success);
            Assert.True((await _service.SaveResolutionConfigAsync(Save((h1, 0m), (h2, 6m)), _collegeId)).Success);
            Assert.Equal(6, _context.Resolution.Single(r => r.SubjectCreditID == h2).Resolution);
            Assert.Equal(0, _context.Resolution.Single(r => r.SubjectCreditID == h1).Resolution);

            // A request that would leave the stored non-zero head AND a new one is rejected too.
            var partial = await _service.SaveResolutionConfigAsync(Save((h1, 2m)), _collegeId);
            Assert.False(partial.Success);
        }

        [Fact]
        public async Task SaveResolutionConfigAsync_keeps_one_row_when_it_finds_duplicates_for_a_head()
        {
            var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20));
            AddStudent("ST001", subject, 10);
            var h1 = subject.Heads.Single().Id;
            _context.Resolution.AddRange(
                new ResolutionMaster { ID = Guid.NewGuid(), CollegeId = _collegeId, ExamID = _examId, SubjectCreditID = h1, Resolution = 9, CreatedAt = DateTime.UtcNow.AddDays(-3) },
                new ResolutionMaster { ID = Guid.NewGuid(), CollegeId = _collegeId, ExamID = _examId, SubjectCreditID = h1, Resolution = 4, CreatedAt = DateTime.UtcNow.AddDays(-1) },
                new ResolutionMaster { ID = Guid.NewGuid(), CollegeId = _collegeId, ExamID = _examId, SubjectCreditID = h1, Resolution = 1, CreatedAt = DateTime.UtcNow.AddDays(-2) });
            await _context.SaveChangesAsync();

            var result = await _service.SaveResolutionConfigAsync(Save((h1, 8m)), _collegeId);

            Assert.True(result.Success, result.Message);
            var row = Assert.Single(_context.Resolution.Where(r => r.SubjectCreditID == h1));
            Assert.Equal(8, row.Resolution);
        }

        [Fact]
        public async Task SaveResolutionConfigAsync_rejects_an_unknown_head_a_head_outside_the_exam_and_a_locked_exam()
        {
            var subject = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20));
            AddStudent("ST001", subject, 10);
            var elsewhere = AddSubject(PassingStrategies.HeadWise, null, ("H1", 50, 20)); // no marks in this exam
            await _context.SaveChangesAsync();

            var unknown = await _service.SaveResolutionConfigAsync(Save((Guid.NewGuid(), 1m)), _collegeId);
            var outside = await _service.SaveResolutionConfigAsync(Save((elsewhere.Heads.Single().Id, 1m)), _collegeId);
            _context.Exams.Single(e => e.ExamId == _examId).IsLocked = true;
            await _context.SaveChangesAsync();
            var locked = await _service.SaveResolutionConfigAsync(Save((subject.Heads.Single().Id, 1m)), _collegeId);

            Assert.False(unknown.Success);
            Assert.False(outside.Success);
            Assert.False(locked.Success);
            Assert.Contains("locked", locked.Message);
            Assert.Empty(_context.Resolution);
        }

        // ------------------------------------------------------------------ helpers

        private sealed record SubjectSetup(Guid SubjectId, Guid CreditsId, List<SubjectCredits> Heads);

        private SubjectSetup AddSubject(string strategy, int? passPercentage, params (string Head, int OutOf, int Pass)[] heads)
        {
            var subjectId = Guid.NewGuid();
            var creditsId = Guid.NewGuid();
            _context.SubjectMasters.Add(new SubjectMaster
            {
                SubjectId = subjectId, SubjectCode = $"S{subjectId.ToString("N")[..4]}", Name = "Subject", CourseId = _courseId,
                SemId = Semester, Pattern = Pattern, CollegeId = _collegeId
            });
            _context.SubjectCreditMasters.Add(new SubjectCreditMaster
            {
                CreditsId = creditsId, SubjectId = subjectId, CollegeId = _collegeId, TotalCredits = "4",
                PassingStrategy = strategy, PassPercentage = passPercentage
            });
            var configs = heads.Select(h => new SubjectCredits
            {
                Id = Guid.NewGuid(), CreditsId = creditsId, Head = h.Head, HeadType = h.Head == "H1" ? "ESE" : "IA",
                HeadOutOf = h.OutOf.ToString(), HeadPass = h.Pass.ToString()
            }).ToList();
            _context.SubjectCredits.AddRange(configs);
            return new SubjectSetup(subjectId, creditsId, configs);
        }

        private List<StudentMarks> AddStudent(string studentId, SubjectSetup subject, params int[] raws)
        {
            var student = _context.StudentMasters.Local.FirstOrDefault(s => s.StudentId == studentId);
            if (student == null)
            {
                student = new StudentMaster
                {
                    StdMstId = Guid.NewGuid(), StudentId = studentId, FirstName = "Test", LastName = studentId, CollegeId = _collegeId
                };
                _context.StudentMasters.Add(student);
            }

            var master = _context.MarksMasters.Local.FirstOrDefault(m => m.StudentID == studentId);
            if (master == null)
            {
                master = new MarksMaster
                {
                    MarksId = Guid.NewGuid(), StudentID = studentId, StdMstId = student.StdMstId, Student = student,
                    ExamId = _examId, SemesterId = Semester, Pattern = Pattern, CollegeId = _collegeId, SeatNo = studentId
                };
                _context.MarksMasters.Add(master);
            }

            var rows = new List<StudentMarks>();
            for (var i = 0; i < subject.Heads.Count; i++)
            {
                var value = i < raws.Length ? raws[i] : (int?)null;
                var row = new StudentMarks
                {
                    Id = Guid.NewGuid(), MarksId = master.MarksId, SubjectId = subject.SubjectId, CreditsId = subject.CreditsId,
                    Head = subject.Heads[i].Head, RawMarks = value, Marks = value
                };
                rows.Add(row);
                _context.StudentMarks.Add(row);
            }
            return rows;
        }

        private void AddLimit(SubjectSetup subject, string head, int limit) =>
            _context.Resolution.Add(new ResolutionMaster
            {
                ID = Guid.NewGuid(), CollegeId = _collegeId, ExamID = _examId, CreditID = subject.CreditsId,
                SubjectCreditID = subject.Heads.Single(h => h.Head == head).Id,
                Resolution = limit, CreatedAt = DateTime.UtcNow
            });

        private SaveMarksRequest SaveRequest(params (Guid Id, string Marks)[] updates) => new()
        {
            ExamId = _examId,
            Updates = updates.Select(u => new StudentMarksUpdateDto { StudentMarksId = u.Id, Marks = u.Marks }).ToList()
        };

        private ResolutionConfigRequest Config() => new()
        {
            BranchId = _courseId, SemId = Semester, Pattern = Pattern, ExamId = _examId
        };

        private SaveResolutionConfigRequest Save(params (Guid CreditId, decimal? Limit)[] limits) => new()
        {
            ExamId = _examId,
            Limits = limits.Select(l => new ResolutionLimitDto { SubjectCreditId = l.CreditId, Limit = l.Limit }).ToList()
        };

        /// <summary>An Excel file in the import template format that sets every given head to 35.</summary>
        private static byte[] BuildTemplate(params StudentMarks[] heads)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Marks");
            ws.Cell(1, 1).Value = "Dummy";
            ws.Cell(6, 4).Value = "H1";
            ws.Cell(6, 5).Value = "H1_ID";
            for (var i = 0; i < heads.Length; i++)
            {
                ws.Cell(7 + i, 4).Value = "35";
                ws.Cell(7 + i, 5).Value = heads[i].Id.ToString();
            }
            return ExamAPI.Services.Report.ExcelStyles.ToBytes(workbook);
        }
    }
}
