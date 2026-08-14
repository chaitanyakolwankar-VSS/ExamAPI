using System;
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
using OfficeOpenXml;
using ExamAPI.DTOs;
using ExamAPI.Services.Tenancy;

namespace ExamAPI.Tests
{
    public class MarksEntryServiceTests
    {
        private readonly MarksEntryService _service;
        private readonly ApplicationDbContext _context;

        /// <summary>
        /// The tenant the context filters every query to. Set it before seeding, or the global
        /// college filter hides the rows the test just added.
        /// </summary>
        private Guid? _currentCollegeId;

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

            ExcelPackage.License.SetNonCommercialPersonal("ReactApi Project");
        }

        [Fact]
        public async Task ImportMarksExcelAsync_AppliesResolutionGraceCorrectly()
        {
            // Arrange
            var collegeId = Guid.NewGuid();
            _currentCollegeId = collegeId;
            var examId = Guid.NewGuid();
            var subjectId = Guid.NewGuid();
            var creditsId = Guid.NewGuid();
            
            var student = new StudentMaster 
            { 
                StdMstId = Guid.NewGuid(), 
                StudentId = "ST001", 
                FirstName = "Test", 
                LastName = "User", 
                CollegeId = collegeId 
            };

            var marksMaster = new MarksMaster 
            { 
                MarksId = Guid.NewGuid(), 
                ExamId = examId,
                StdMstId = student.StdMstId,
                Student = student,
                StudentID = student.StudentId
            };

            var headCredit = new SubjectCredits { Id = Guid.NewGuid(), Head = "H1", HeadType = "ESE", HeadPass = "40", HeadOutOf = "100" };
            var creditMaster = new SubjectCreditMaster
            {
                CreditsId = creditsId,
                Credits = new System.Collections.Generic.List<SubjectCredits> { headCredit }
            };
            
            // Student scored 35, needs 40. We will set up a Resolution Rule allowing up to 5 grace
            var studentMarks = new StudentMarks
            {
                Id = Guid.NewGuid(),
                MarksId = marksMaster.MarksId,
                SubjectId = subjectId,
                Head = "H1",
                CreditsId = creditsId,
                MarksMaster = marksMaster,
                CreditMaster = creditMaster
            };

            var resolution = new ResolutionMaster
            {
                ID = Guid.NewGuid(),
                ExamID = examId,
                CreditID = creditsId,
                SubjectCreditID = headCredit.Id,
                Head = "H1",
                Resolution = "5", // Max 5 marks allowed
                IsDeleted = false
            };

            _context.StudentMasters.Add(student);
            _context.MarksMasters.Add(marksMaster);
            _context.SubjectCreditMasters.Add(creditMaster);
            _context.StudentMarks.Add(studentMarks);
            _context.Resolution.Add(resolution);
            await _context.SaveChangesAsync();

            // Create a fake Excel File matching the Import Template format
            byte[] fileBytes;
            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("Marks");
                ws.Cells[1, 1].Value = "Dummy"; // Ensure Dimension starts at 1,1
                
                // Rows 1-5 are headers/metadata
                // Row 6 contains the column labels (e.g. TH_ID)
                ws.Cells[6, 4].Value = "H1";
                ws.Cells[6, 5].Value = "H1_ID";
                
                // Row 7 contains the data
                ws.Cells[7, 4].Value = "35"; // Student typed 35 marks
                ws.Cells[7, 5].Value = studentMarks.Id.ToString(); // The ID column

                fileBytes = package.GetAsByteArray();
            }

            // Act
            var result = await _service.ImportMarksExcelAsync(examId, subjectId, fileBytes, collegeId);

            // Assert
            Assert.True(result.Success, result.Message);
            
            var updatedSm = await _context.StudentMarks.FindAsync(studentMarks.Id);
            Assert.NotNull(updatedSm);
            
            // Should apply 5 grace to reach 40
            Assert.Equal(35, updatedSm.RawMarks);
            Assert.Equal(40, updatedSm.Marks);
            Assert.Equal(5, updatedSm.Resolution);
            
        }
    }
}
