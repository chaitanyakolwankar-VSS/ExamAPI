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
            ["Asha", "", "Patil", "Sunita", "Open", "202600000000012345", "female"],
            [],
            ["Ravi", "K", "Shah", "Sunita", "", "", "Male"]);

        var result = await _service.ImportStudentsAsync(Import(file));

        Assert.True(Success(result));
        var students = await _context.StudentMasters.OrderBy(s => s.StudentId).ToListAsync();
        Assert.Equal(2, students.Count);
        Assert.Equal("202600000000012345", students[0].StudentPRN); // long ERN kept as text
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
            ["Asha", "", "Patil", "Sunita", "", "111", "Female"],   // ERN already used
            ["Ravi", "", "", "Sunita", "", "222", "Male"],          // no last name
            ["Mina", "", "Rao", "Sunita", "", "222", "F"],          // ERN twice in file, bad gender
            ["Gen", "", "Eral", "Sunita", "General", "444", "Male"],       // category not in the list
            ["Ok", "", "Student", "Sunita", "obc", "333", "Male"]);

        var result = await _service.ImportStudentsAsync(Import(file));

        Assert.False(Success(result));
        var errors = Errors(result);
        Assert.Contains(errors, e => e.StartsWith("Row 3: ERN 111") && e.Contains("already belongs"));
        Assert.Contains(errors, e => e.StartsWith("Row 4:") && e.Contains("last name"));
        Assert.Contains(errors, e => e.StartsWith("Row 5: ERN 222") && e.Contains("twice"));
        Assert.Contains(errors, e => e.StartsWith("Row 5:") && e.Contains("Male or Female"));
        Assert.Contains(errors, e => e.StartsWith("Row 6:") && e.Contains("not in the list"));
        Assert.DoesNotContain(errors, e => e.StartsWith("Row 7:"));
        Assert.Equal(1, await _context.StudentMasters.CountAsync()); // nothing imported
    }

    private static IFormFile WithHeader(IFormFile file, int column, string header)
    {
        using var workbook = new XLWorkbook(file.OpenReadStream());
        workbook.Worksheet(1).Cell(2, column).Value = header;
        var output = new MemoryStream();
        workbook.SaveAs(output);
        output.Position = 0;
        return new FormFile(output, 0, output.Length, "File", "students.xlsx");
    }

    [Fact]
    public async Task Template_says_ERN_and_an_old_PRN_template_still_imports()
    {
        var (bytes, _) = await _service.GenerateExcelTemplateAsync(new StudExcelDto { CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay });
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
            Assert.Equal("ERN", workbook.Worksheet(1).Cell(2, 6).GetString());

        var oldTemplate = WithHeader(await FilledTemplate(["Asha", "", "Patil", "Sunita", "", "555", "Female"]), 6, "PRN");

        Assert.True(Success(await _service.ImportStudentsAsync(Import(oldTemplate))));
        Assert.Equal("555", (await _context.StudentMasters.SingleAsync()).StudentPRN);
    }

    [Fact]
    public async Task A_file_with_another_heading_in_the_ERN_column_is_refused()
    {
        var file = WithHeader(await FilledTemplate(["Asha", "", "Patil", "Sunita", "", "555", "Female"]), 6, "Roll No");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ImportStudentsAsync(Import(file)));
        Assert.Contains("not the student template", ex.Message);
    }

    private static IFormFile RawFile(string[] headers, params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Students");
        for (int c = 0; c < headers.Length; c++)
            sheet.Cell(2, c + 1).Value = headers[c];
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                sheet.Cell(r + 3, c + 1).Value = rows[r][c];
        var output = new MemoryStream();
        workbook.SaveAs(output);
        output.Position = 0;
        return new FormFile(output, 0, output.Length, "File", "students.xlsx");
    }

    [Fact]
    public async Task Template_has_the_MotherName_column_after_LastName()
    {
        var (bytes, _) = await _service.GenerateExcelTemplateAsync(new StudExcelDto { CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay });
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["FirstName", "MiddleName", "LastName", "MotherName", "Category", "ERN", "Gender"],
            Enumerable.Range(1, 7).Select(c => workbook.Worksheet(1).Cell(2, c).GetString()));
    }

    [Fact]
    public async Task Mother_name_is_saved_with_the_imported_student()
    {
        var file = await FilledTemplate(["Asha", "", "Patil", "  Sunita Ramesh ", "", "", "Female"]);

        Assert.True(Success(await _service.ImportStudentsAsync(Import(file))));
        Assert.Equal("Sunita Ramesh", (await _context.StudentMasters.SingleAsync()).MotherName);
    }

    [Fact]
    public async Task A_row_without_the_mother_name_is_reported_and_nothing_is_imported()
    {
        var file = await FilledTemplate(
            ["Asha", "", "Patil", "Sunita", "", "", "Female"],
            ["Ravi", "", "Shah", "", "", "", "Male"],
            ["Mina", "", "Rao", new string('x', 51), "", "", "Female"]);

        var result = await _service.ImportStudentsAsync(Import(file));

        Assert.False(Success(result));
        var errors = Errors(result);
        Assert.Contains("Row 4: mother's name is required.", errors);
        Assert.Contains(errors, e => e.StartsWith("Row 5:") && e.Contains("50 characters"));
        Assert.DoesNotContain(errors, e => e.StartsWith("Row 3:"));
        Assert.Equal(0, await _context.StudentMasters.CountAsync());
    }

    [Fact]
    public async Task An_old_template_without_the_MotherName_column_is_refused_with_a_clear_message()
    {
        var file = RawFile(["FirstName", "MiddleName", "LastName", "Category", "PRN", "Gender"],
            ["Asha", "", "Patil", "", "555", "Female"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ImportStudentsAsync(Import(file)));
        Assert.Contains("no MotherName column", ex.Message);
        Assert.Equal(0, await _context.StudentMasters.CountAsync());
    }

    [Fact]
    public async Task Columns_are_found_by_heading_not_by_position()
    {
        var file = RawFile(["gender", " ERN ", "MotherName", "LastName", "FirstName", "Category", "MiddleName"],
            ["Female", "777", "Sunita", "Patil", "Asha", "obc", "R"]);

        Assert.True(Success(await _service.ImportStudentsAsync(Import(file))));
        var student = await _context.StudentMasters.SingleAsync();
        Assert.Equal(("Asha", "R", "Patil", "Sunita"), (student.FirstName, student.MiddleName, student.LastName, student.MotherName));
        Assert.Equal(("Female", "777", "OBC"), (student.Gender, student.StudentPRN, student.Category));
    }

    [Fact]
    public async Task Saving_a_student_without_the_mother_name_is_refused()
    {
        var dto = new Savedata { CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay, FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "  " };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(dto));
        Assert.Equal("Enter the mother's name.", ex.Message);
        Assert.Equal(0, await _context.StudentMasters.CountAsync());
    }

    [Fact]
    public async Task Mother_name_is_trimmed_saved_returned_and_required_when_editing()
    {
        var id = await _service.SaveStudentAsync(new Savedata
        {
            CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay,
            FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "  Sunita ",
        });
        Assert.Equal("Sunita", (await _service.GetStudentByIdAsync(id, _ay)).MotherName);

        var edit = new Savedata { StudentId = id, CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay, FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "" };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateStudentAsync(edit));
        Assert.Equal("Enter the mother's name.", ex.Message);

        edit.MotherName = " Meena ";
        await _service.UpdateStudentAsync(edit);
        Assert.Equal("Meena", (await _service.GetStudentByIdAsync(id, _ay)).MotherName);
    }

    [Fact]
    public async Task Import_without_a_pattern_is_refused()
    {
        var file = await FilledTemplate(["Asha", "", "Patil", "Sunita", "", "", "Female"]);
        var dto = Import(file);
        dto.Pattern = "";

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ImportStudentsAsync(dto));
    }

    [Fact]
    public async Task Saving_a_student_without_a_pattern_is_refused()
    {
        var dto = new Savedata { CourseId = _course, SemesterId = "Sem-1", AYID = _ay, FirstName = "Asha", LastName = "Patil", MotherName = "Sunita", Gender = "Female", StudentPRN = "9" };

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
            FirstName = "A", LastName = "B", MotherName = "M", Gender = "Male", StudentPRN = prn,
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

        var file = await FilledTemplate(["Asha", "", "Patil", "Sunita", "", "", "Female"], ["Ravi", "", "Shah", "Sunita", "", "", "Male"]);
        Assert.True(Success(await _service.ImportStudentsAsync(Import(file))));
        Assert.True(await _context.StudentMasters.AnyAsync(s => s.StudentId == "26IL0009"));
        Assert.True(await _context.StudentMasters.AnyAsync(s => s.StudentId == "26IL0010"));
    }
}
