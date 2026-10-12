using ClosedXML.Excel;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Files;
using ExamAPI.Services.StudentMasters;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>Student Master Excel: the downloaded template imports, students get the pattern, bad rows stop the import.</summary>
public sealed class StudentExcelImportTests
{
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _course = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly StudentMasterService _service;

    public StudentExcelImportTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _context.CourseMasters.Add(new CourseMaster { CourseId = _course, Name = "B.Tech. Civil Engineering", CourseCode = "CIVIL", CollegeId = _college });
        _context.AcademicYears.Add(new AcademicYear { AYID = _ay, FullDuration = "2026-2027", ShortDuration = "2026-27", IsCurrent = true, CollegeId = _college });
        _context.SaveChanges();
        _service = new StudentMasterService(_context, new Mock<IFileStorage>().Object);
    }

    private async Task<IFormFile> FilledTemplate(params string[][] rows)
    {
        var (bytes, name) = await _service.GenerateExcelTemplateAsync(new StudExcelDto { CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay });
        Assert.StartsWith("Students B.Tech. Civil Engineering Sem-1 NEP", name);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                sheet.Cell(r + 3, c + 1).Value = rows[r][c];
        var output = new MemoryStream();
        workbook.SaveAs(output);
        output.Position = 0;
        return new FormFile(output, 0, output.Length, "File", "students.xlsx");
    }

    private StudentImportDto Import(IFormFile file) => new() { File = file, CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay };

    private static bool Success(object result) => (bool)result.GetType().GetProperty("Success")!.GetValue(result)!;
    private static List<string> Errors(object result) => (List<string>)result.GetType().GetProperty("Errors")!.GetValue(result)!;

    [Fact]
    public async Task Template_rows_become_students_enrolled_with_the_pattern()
    {
        var file = await FilledTemplate(
            ["Asha", "", "Patil", "Open", "202600000000012345", "female"],
            [],
            ["Ravi", "K", "Shah", "", "", "Male"]);

        var result = await _service.ImportStudentsAsync(Import(file));

        Assert.True(Success(result));
        var students = await _context.StudentMasters.OrderBy(s => s.StudentId).ToListAsync();
        Assert.Equal(2, students.Count);
        Assert.Equal("202600000000012345", students[0].StudentPRN); // long PRN kept as text
        Assert.Equal("Female", students[0].Gender);
        Assert.Equal("OPEN", students[0].Category);                 // "Open" typed, list spelling stored
        Assert.All(await _context.StudentEligibilities.ToListAsync(), e =>
        {
            Assert.Equal("NEP", e.Pattern);
            Assert.Equal("Sem-1", e.SemesterId);
            Assert.Equal(_course, e.CourseId);
        });
    }

    [Fact]
    public async Task Any_bad_row_stops_the_whole_import_with_one_line_per_problem()
    {
        _context.StudentMasters.Add(new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "OLD1", FirstName = "Old", LastName = "One", StudentPRN = "111", CollegeId = _college });
        await _context.SaveChangesAsync();
        var file = await FilledTemplate(
            ["Asha", "", "Patil", "", "111", "Female"],   // PRN already used
            ["Ravi", "", "", "", "222", "Male"],          // no last name
            ["Mina", "", "Rao", "", "222", "F"],          // PRN twice in file, bad gender
            ["Gen", "", "Eral", "General", "444", "Male"],       // category not in the list
            ["Ok", "", "Student", "obc", "333", "Male"]);

        var result = await _service.ImportStudentsAsync(Import(file));

        Assert.False(Success(result));
        var errors = Errors(result);
        Assert.Contains(errors, e => e.StartsWith("Row 3:") && e.Contains("already belongs"));
        Assert.Contains(errors, e => e.StartsWith("Row 4:") && e.Contains("last name"));
        Assert.Contains(errors, e => e.StartsWith("Row 5:") && e.Contains("twice"));
        Assert.Contains(errors, e => e.StartsWith("Row 5:") && e.Contains("Male or Female"));
        Assert.Contains(errors, e => e.StartsWith("Row 6:") && e.Contains("not in the list"));
        Assert.DoesNotContain(errors, e => e.StartsWith("Row 7:"));
        Assert.Equal(1, await _context.StudentMasters.CountAsync()); // nothing imported
    }

    [Fact]
    public async Task Import_without_a_pattern_is_refused()
    {
        var file = await FilledTemplate(["Asha", "", "Patil", "", "", "Female"]);
        var dto = Import(file);
        dto.Pattern = "";

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ImportStudentsAsync(dto));
    }

    [Fact]
    public async Task Saving_a_student_without_a_pattern_is_refused()
    {
        var dto = new Savedata { CourseId = _course, SemesterId = "Sem-1", AYID = _ay, FirstName = "Asha", LastName = "Patil", Gender = "Female", StudentPRN = "9" };

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => _service.SaveStudentAsync(dto));
        Assert.Contains("pattern", ex.Message);
    }

    // ---------------------------------------------------------------- student IDs

    private Guid Branch(string code)
    {
        var id = Guid.NewGuid();
        _context.CourseMasters.Add(new CourseMaster { CourseId = id, Name = code, CourseCode = code, CollegeId = _college });
        _context.SaveChanges();
        return id;
    }

    private async Task<string> Save(Guid course, string prn) =>
        await _service.SaveStudentAsync(new Savedata
        {
            CourseId = course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay,
            FirstName = "A", LastName = "B", Gender = "Male", StudentPRN = prn,
        });

    [Fact]
    public async Task Ids_are_year_two_clean_characters_and_a_running_number()
    {
        Assert.Equal("26IL0001", await Save(_course, "1"));            // CIVIL
        Assert.Equal("26IL0002", await Save(_course, "2"));
        Assert.Equal("26SE0001", await Save(Branch("MTECH-MSE"), "3"));  // hyphen dropped
        Assert.Equal("26ML0001", await Save(Branch("cse-aiml"), "4"));   // upper case
    }

    [Fact]
    public async Task Branches_whose_codes_end_alike_share_one_counter()
    {
        var ece = Branch("ECE");
        var ce = Branch("CE");

        Assert.Equal("26CE0001", await Save(ece, "1"));
        Assert.Equal("26CE0002", await Save(ce, "2"));
        Assert.Equal("26CE0003", await Save(ece, "3"));
    }

    [Fact]
    public async Task Numbering_continues_after_existing_and_deleted_students()
    {
        _context.StudentMasters.Add(new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "26IL0007", FirstName = "Old", LastName = "Gone", IsDeleted = true, CollegeId = _college });
        _context.StudentMasters.Add(new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "26IL0004", FirstName = "Old", LastName = "Here", CollegeId = _college });
        _context.StudentMasters.Add(new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "26IL0099", FirstName = "Other", LastName = "College", CollegeId = Guid.NewGuid() });
        await _context.SaveChangesAsync();

        Assert.Equal("26IL0008", await Save(_course, "1"));

        var file = await FilledTemplate(["Asha", "", "Patil", "", "", "Female"], ["Ravi", "", "Shah", "", "", "Male"]);
        Assert.True(Success(await _service.ImportStudentsAsync(Import(file))));
        Assert.True(await _context.StudentMasters.AnyAsync(s => s.StudentId == "26IL0009"));
        Assert.True(await _context.StudentMasters.AnyAsync(s => s.StudentId == "26IL0010"));
    }
}
