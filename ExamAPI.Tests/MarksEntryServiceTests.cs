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

namespace ExamAPI.Tests
{
    public class MarksEntryServiceTests
    {
        private readonly MarksEntryService _service;
        private readonly ApplicationDbContext _context;

        public MarksEntryServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
            _context = new ApplicationDbContext(options, mockHttpContextAccessor.Object);
            
            _service = new MarksEntryService(_context);
            
            ExcelPackage.License.SetNonCommercialPersonal("ReactApi Project");
        }

        [Fact]
        public async Task ImportMarksExcelAsync_AppliesResolutionGraceCorrectly()
        {
            // Arrange
            var collegeId = Guid.NewGuid();
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

            var creditMaster = new SubjectCreditMaster 
            { 
                CreditsId = creditsId,
                Credits = new System.Collections.Generic.List<SubjectCredits>
                {
                    new SubjectCredits { Head = "TH", HeadPass = "40", HeadOutOf = "100" }
                }
            };
            
            // Student scored 35, needs 40. We will set up a Resolution Rule allowing up to 5 grace
            var studentMarks = new StudentMarks
            {
                Id = Guid.NewGuid(),
                MarksId = marksMaster.MarksId,
                SubjectId = subjectId,
                Head = "TH",
                CreditsId = creditsId,
                MarksMaster = marksMaster,
                CreditMaster = creditMaster
            };

            var resolution = new ResolutionMaster
            {
                ID = Guid.NewGuid(),
                ExamID = examId,
                CreditID = creditsId,
                Head = "TH",
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
                ws.Cells[6, 4].Value = "TH";
                ws.Cells[6, 5].Value = "TH_ID";
                
                // Row 7 contains the data
                ws.Cells[7, 4].Value = "35"; // Student typed 35 marks
                ws.Cells[7, 5].Value = studentMarks.Id.ToString(); // The ID column

                fileBytes = package.GetAsByteArray();
            }

            // Act
            var result = await _service.ImportMarksExcelAsync(examId, subjectId, fileBytes, collegeId);

            // Assert
            Assert.True(result.Success);
            
            var updatedSm = await _context.StudentMarks.FindAsync(studentMarks.Id);
            Assert.NotNull(updatedSm);
            
            // Should apply 5 grace to reach 40
            Assert.Equal(35, updatedSm.RawMarks);
            Assert.Equal(40, updatedSm.Marks);
            Assert.Equal(5, updatedSm.Resolution);
            Assert.Equal("Successful", updatedSm.Remark);
        }
    }
}
