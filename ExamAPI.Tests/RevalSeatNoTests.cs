using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.AtktRevalExam;
using ExamAPI.Services.Common;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Services.Result.Engine.FactProviders;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ExamAPI.Tests
{
    /// <summary>
    /// DEC-16: a revaluation is only a paper re-check, so its MarksMaster reuses the parent
    /// (source) exam's seat number. ATKT keeps its own behaviour (prefilled from the latest
    /// attempt, then owned by the Seat No screen), which must not change.
    /// </summary>
    public class RevalSeatNoTests
    {
        private const string Semester = "Sem-6";
        private const string Pattern = "NEP";

        private readonly ApplicationDbContext _context;
        private readonly AtktRevalExamService _service;

        private readonly Guid _collegeId = Guid.NewGuid();
        private readonly Guid _ayid = Guid.NewGuid();
        private readonly Guid _courseId = Guid.NewGuid();
        private readonly Guid _patternId = Guid.NewGuid();
        private readonly Guid _parentExamId = Guid.NewGuid();
        private readonly Guid _revalExamId = Guid.NewGuid();
        private readonly Guid _atktExamId = Guid.NewGuid();
        private readonly Guid _studentId = Guid.NewGuid();
        private Guid _subjectId;
        private MarksMaster _parentMarks = null!;

        public RevalSeatNoTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(u => u.CollegeId).Returns(_collegeId);

            _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);

            _service = new AtktRevalExamService(
                _context,
                new GenericRepository(_context),
                new EngineRegistry(
                    new IFactProvider[] { new FailedSubjectCountProvider() },
                    Array.Empty<IActionHandler>()));

            Seed();
        }

        private void Seed()
        {
            _context.Colleges.Add(new College
            {
                CollegeId = _collegeId, Name = "Test College", CollegeCode = "TC",
                CollegeCenter = "Main", ContactEmail = "test@example.com", ContactPhone = "0000000000"
            });
            _context.AcademicYears.Add(new AcademicYear
            {
                AYID = _ayid, CollegeId = _collegeId, ShortDuration = "2024-2025", FullDuration = "2024-2025"
            });
            _context.CourseMasters.Add(new CourseMaster { CourseId = _courseId, Name = "Computer", CourseCode = "CS", CollegeId = _collegeId });
            _context.PatternMasters.Add(new PatternMaster { PatternId = _patternId, PatternName = Pattern, CollegeId = _collegeId });

            _context.Exams.Add(new ExamMaster
            {
                ExamId = _parentExamId, Name = "May 2025", ExamType = "Regular",
                CourseId = _courseId, AcademicYearAYID = _ayid, IsActive = true, CollegeId = _collegeId
            });
            _context.Exams.Add(new ExamMaster
            {
                ExamId = _revalExamId, Name = "May 2025 Reval", ExamType = "Regular",
                RevaluationForExamId = _parentExamId,
                CourseId = _courseId, AcademicYearAYID = _ayid, IsActive = true, CollegeId = _collegeId
            });
            _context.Exams.Add(new ExamMaster
            {
                ExamId = _atktExamId, Name = "ATKT Oct 2025", ExamType = "KT",
                CourseId = _courseId, AcademicYearAYID = _ayid, IsActive = true, CollegeId = _collegeId
            });

            _context.StudentMasters.Add(new StudentMaster
            {
                StdMstId = _studentId, StudentId = "ST001", FirstName = "Asha", LastName = "Rao", CollegeId = _collegeId
            });
            _context.StudentEligibilities.Add(new StudentEligibility
            {
                Id = Guid.NewGuid(), StdMstId = _studentId, StudentId = "ST001",
                CourseId = _courseId, AYID = _ayid, SemesterId = Semester, Pattern = Pattern, CollegeId = _collegeId
            });

            _parentMarks = new MarksMaster
            {
                MarksId = Guid.NewGuid(), StdMstId = _studentId, StudentID = "ST001",
                ExamId = _parentExamId, AcademicYearAYID = _ayid, SemesterId = Semester,
                Pattern = Pattern, SeatNo = "A-101", QuotaType = "Open", CollegeId = _collegeId,
                CreatedAt = DateTime.UtcNow.AddDays(-30)
            };
            _context.MarksMasters.Add(_parentMarks);

            _subjectId = Guid.NewGuid();
            var creditsId = Guid.NewGuid();
            _context.SubjectMasters.Add(new SubjectMaster
            {
                SubjectId = _subjectId, SubjectCode = "SUB1", Name = "Failed Subject",
                SemId = Semester, Pattern = Pattern, CourseId = _courseId, CollegeId = _collegeId
            });
            _context.SubjectCreditMasters.Add(new SubjectCreditMaster
            {
                CreditsId = creditsId, SubjectId = _subjectId, TotalCredits = "4",
                AYID = _ayid.ToString(), PassingStrategy = PassingStrategies.HeadWise, CollegeId = _collegeId,
                Credits = new List<SubjectCredits>
                {
                    new() { Id = Guid.NewGuid(), Head = "H1", HeadType = "ESE", HeadOutOf = "80", HeadPass = "32", CreditsId = creditsId },
                    new() { Id = Guid.NewGuid(), Head = "H2", HeadType = "IA", HeadOutOf = "20", HeadPass = "8", CreditsId = creditsId }
                }
            });
            _context.StudentMarks.Add(new StudentMarks
            {
                Id = Guid.NewGuid(), MarksId = _parentMarks.MarksId, SubjectId = _subjectId,
                CreditsId = creditsId, Head = "H1", Marks = 20, RawMarks = 20
            });
            _context.StudentMarks.Add(new StudentMarks
            {
                Id = Guid.NewGuid(), MarksId = _parentMarks.MarksId, SubjectId = _subjectId,
                CreditsId = creditsId, Head = "H2", Marks = 15, RawMarks = 15
            });

            _context.SaveChanges();
        }

        private AtktMatrixRequest Request(string mode, Guid targetExamId) => new()
        {
            CourseId = _courseId,
            Ayid = _ayid,
            Semester = Semester,
            Pattern = Pattern,
            Mode = mode,
            TargetExamId = targetExamId,
            SourceExamId = AssignmentModes.IsRevaluation(mode) ? _parentExamId : null
        };

        private Task<ApiResponseDto<AtktSaveResultDto>> Save(string mode, Guid targetExamId) =>
            _service.SaveAsync(new AtktSaveRequest
            {
                Filter = Request(mode, targetExamId),
                Students = new List<AtktStudentSelectionDto>
                {
                    new() { StdMstId = _studentId, SubjectIds = new List<Guid> { _subjectId } }
                }
            });

        [Fact]
        public async Task Reval_Assignment_CopiesParentSeatNoAndQuota()
        {
            var response = await Save(AssignmentModes.Revaluation, _revalExamId);

            Assert.True(response.Success);
            Assert.Equal(1, response.Data!.StudentsAssigned);

            var reval = Assert.Single(_context.MarksMasters.Where(mm => mm.ExamId == _revalExamId));
            Assert.Equal("A-101", reval.SeatNo);
            Assert.Equal(_parentMarks.SeatNo, reval.SeatNo);
            Assert.Equal("Open", reval.QuotaType);
        }

        [Fact]
        public async Task Reval_Resave_ReSyncsSeatNoWithParent()
        {
            await Save(AssignmentModes.Revaluation, _revalExamId);

            // Parent seat changes (e.g. corrected on the Seat No screen) and the reval row drifts.
            _parentMarks.SeatNo = "A-202";
            var reval = _context.MarksMasters.Single(mm => mm.ExamId == _revalExamId);
            reval.SeatNo = "STALE";
            await _context.SaveChangesAsync();

            await Save(AssignmentModes.Revaluation, _revalExamId);

            Assert.Equal("A-202", _context.MarksMasters.Single(mm => mm.ExamId == _revalExamId).SeatNo);
        }

        [Fact]
        public async Task Reval_Matrix_ShowsParentSeatNoBeforeAssignment()
        {
            var matrix = await _service.GetMatrixAsync(Request(AssignmentModes.Revaluation, _revalExamId));

            Assert.True(matrix.Success);
            Assert.Equal("A-101", Assert.Single(matrix.Students).SeatNo);
        }

        [Fact]
        public async Task Reval_Matrix_RejectsASourceThatIsNotTheParent()
        {
            var request = Request(AssignmentModes.Revaluation, _revalExamId);
            request.SourceExamId = _atktExamId;

            var matrix = await _service.GetMatrixAsync(request);

            Assert.False(matrix.Success);
            Assert.Contains("does not belong", matrix.Message);
        }

        [Fact]
        public async Task Atkt_Assignment_KeepsExistingBehaviour_PrefillsFromLatestAttempt()
        {
            var response = await Save(AssignmentModes.Atkt, _atktExamId);

            Assert.True(response.Success);

            // Current behaviour: the new ATKT row is prefilled with the source attempt's seat no,
            // and the Seat No screen then owns it.
            var atkt = Assert.Single(_context.MarksMasters.Where(mm => mm.ExamId == _atktExamId));
            Assert.Equal("A-101", atkt.SeatNo);
        }

        [Fact]
        public async Task Atkt_Resave_DoesNotOverwriteSeatNoSetOnSeatNoScreen()
        {
            await Save(AssignmentModes.Atkt, _atktExamId);

            var atkt = _context.MarksMasters.Single(mm => mm.ExamId == _atktExamId);
            atkt.SeatNo = "K-900"; // fresh ATKT seat number assigned through the Seat No screen
            await _context.SaveChangesAsync();

            await Save(AssignmentModes.Atkt, _atktExamId);

            Assert.Equal("K-900", _context.MarksMasters.Single(mm => mm.ExamId == _atktExamId).SeatNo);
        }
    }
}
