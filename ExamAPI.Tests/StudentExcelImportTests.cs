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
            ["Asha", "", "Patil", "Sunita", "Open", "202600000000012345", "female", "E"],
            [],
            ["Ravi", "K", "Shah", "Sunita", "", "", "Male", "E"]);

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
            ["Asha", "", "Patil", "Sunita", "", "111", "Female", "E"],   // ERN already used
            ["Ravi", "", "", "Sunita", "", "222", "Male", "E"],          // no last name: allowed
            ["Mina", "", "Rao", "Sunita", "", "222", "F", "E"],          // ERN twice in file, bad gender
            ["Gen", "", "Eral", "Sunita", "General", "444", "Male", "E"],       // category not in the list
            ["Ok", "", "Student", "Sunita", "obc", "333", "Male", "E"]);

        var result = await _service.ImportStudentsAsync(Import(file));

        Assert.False(Success(result));
        var errors = Errors(result);
        Assert.Contains(errors, e => e.StartsWith("Row 3: ERN 111") && e.Contains("already belongs"));
        Assert.DoesNotContain(errors, e => e.StartsWith("Row 4:"));
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

        var oldTemplate = WithHeader(await FilledTemplate(["Asha", "", "Patil", "Sunita", "", "555", "Female", "E"]), 6, "PRN");

        Assert.True(Success(await _service.ImportStudentsAsync(Import(oldTemplate))));
        Assert.Equal("555", (await _context.StudentMasters.SingleAsync()).StudentPRN);
    }

    [Fact]
    public async Task A_file_with_another_heading_in_the_ERN_column_is_refused()
    {
        var file = WithHeader(await FilledTemplate(["Asha", "", "Patil", "Sunita", "", "555", "Female", "E"]), 6, "Roll No");

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
    public async Task Template_has_the_MotherName_column_after_LastName_and_StudentType_last()
    {
        var (bytes, _) = await _service.GenerateExcelTemplateAsync(new StudExcelDto { CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay });
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["FirstName", "MiddleName", "LastName", "MotherName", "Category", "ERN", "Gender", "StudentType"],
            Enumerable.Range(1, 8).Select(c => workbook.Worksheet(1).Cell(2, c).GetString()));
    }

    [Fact]
    public async Task Mother_name_is_saved_with_the_imported_student()
    {
        var file = await FilledTemplate(["Asha", "", "Patil", "  Sunita Ramesh ", "", "", "Female", "E"]);

        Assert.True(Success(await _service.ImportStudentsAsync(Import(file))));
        Assert.Equal("Sunita Ramesh", (await _context.StudentMasters.SingleAsync()).MotherName);
    }

    [Fact]
    public async Task A_row_without_the_mother_name_is_reported_and_nothing_is_imported()
    {
        var file = await FilledTemplate(
            ["Asha", "", "Patil", "Sunita", "", "", "Female", "E"],
            ["Ravi", "", "Shah", "", "", "", "Male", "E"],
            ["Mina", "", "Rao", new string('x', 51), "", "", "Female", "E"]);

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
        var file = RawFile(["gender", " ERN ", "MotherName", "LastName", "FirstName", "Category", "MiddleName", "studenttype"],
            ["Female", "777", "Sunita", "Patil", "Asha", "obc", "R", "m"]);

        Assert.True(Success(await _service.ImportStudentsAsync(Import(file))));
        var student = await _context.StudentMasters.SingleAsync();
        Assert.Equal(("Asha", "R", "Patil", "Sunita"), (student.FirstName, student.MiddleName, student.LastName, student.MotherName));
        Assert.Equal(("Female", "777", "OBC"), (student.Gender, student.StudentPRN, student.Category));
    }

    [Fact]
    public async Task Saving_a_student_without_the_mother_name_is_refused()
    {
        var dto = new Savedata { CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay, FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "  ", StudentType = "E" };

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
            FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "  Sunita ", StudentType = "E",
        });
        Assert.Equal("Sunita", (await _service.GetStudentByIdAsync(id, _ay)).MotherName);

        var edit = new Savedata { StudentId = id, CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay, FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "", StudentType = "E" };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateStudentAsync(edit));
        Assert.Equal("Enter the mother's name.", ex.Message);

        edit.MotherName = " Meena ";
        await _service.UpdateStudentAsync(edit);
        Assert.Equal("Meena", (await _service.GetStudentByIdAsync(id, _ay)).MotherName);
    }

    [Fact]
    public async Task Student_type_is_stored_as_a_letter_whether_typed_as_letter_or_word()
    {
        var file = await FilledTemplate(
            ["Asha", "", "Patil", "Sunita", "", "", "Female", " d "],
            ["Ravi", "", "Shah", "Sunita", "", "", "Male", "Engineering"],
            ["Mina", "", "Rao", "Sunita", "", "", "Female", "mca"]);

        Assert.True(Success(await _service.ImportStudentsAsync(Import(file))));
        var types = await _context.StudentMasters.OrderBy(s => s.StudentId).Select(s => s.StudentType).ToListAsync();
        Assert.Equal(["D", "E", "M"], types);
    }

    [Fact]
    public async Task A_row_without_or_with_a_wrong_student_type_is_reported_and_nothing_is_imported()
    {
        var file = await FilledTemplate(
            ["Asha", "", "Patil", "Sunita", "", "", "Female", "E"],
            ["Ravi", "", "Shah", "Sunita", "", "", "Male", ""],
            ["Mina", "", "Rao", "Sunita", "", "", "Female", "X"]);

        var result = await _service.ImportStudentsAsync(Import(file));

        Assert.False(Success(result));
        var errors = Errors(result);
        Assert.Contains("Row 4: student type is required (E, D or M).", errors);
        Assert.Contains("Row 5: student type must be E, D or M.", errors);
        Assert.DoesNotContain(errors, e => e.StartsWith("Row 3:"));
        Assert.Equal(0, await _context.StudentMasters.CountAsync());
    }

    [Fact]
    public async Task A_template_without_the_StudentType_column_is_refused_with_a_clear_message()
    {
        var file = RawFile(["FirstName", "MiddleName", "LastName", "MotherName", "Category", "ERN", "Gender"],
            ["Asha", "", "Patil", "Sunita", "", "555", "Female"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ImportStudentsAsync(Import(file)));
        Assert.Equal("This template has no StudentType column. Download the template again.", ex.Message);
        Assert.Equal(0, await _context.StudentMasters.CountAsync());
    }

    [Theory]
    [InlineData("E", "E")]
    [InlineData(" diploma ", "D")]
    [InlineData("MCA", "M")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("X", null)]
    [InlineData("Engg", null)]
    public void Student_type_input_is_normalised(string? input, string? expected) =>
        Assert.Equal(expected, StudentMasterService.NormalizeStudentType(input));

    [Fact]
    public async Task Saving_or_editing_a_student_without_a_valid_type_is_refused()
    {
        var dto = new Savedata { CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay, FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "Sunita", StudentType = " " };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(dto));
        Assert.Equal("Choose the student type (Engineering, Diploma or MCA).", ex.Message);
        Assert.Equal(0, await _context.StudentMasters.CountAsync());

        dto.StudentType = "diploma";
        var id = await _service.SaveStudentAsync(dto);

        var edit = new Savedata { StudentId = id, CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay, FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "Sunita", StudentType = "Z" };
        ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateStudentAsync(edit));
        Assert.Equal("Choose the student type (Engineering, Diploma or MCA).", ex.Message);
        Assert.Equal("D", (await _service.GetStudentByIdAsync(id, _ay)).StudentType);

        edit.StudentType = "m";
        await _service.UpdateStudentAsync(edit);
        Assert.Equal("M", (await _service.GetStudentByIdAsync(id, _ay)).StudentType);
        Assert.Equal("M", (await _service.SearchStudentsAsync(new Searchbyname { StudentPRN = "", StudentId = id }, _ay)).Single().StudentType);
    }

    [Fact]
    public async Task Import_without_a_pattern_is_refused()
    {
        var file = await FilledTemplate(["Asha", "", "Patil", "Sunita", "", "", "Female", "E"]);
        var dto = Import(file);
        dto.Pattern = "";

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ImportStudentsAsync(dto));
    }

    [Fact]
    public async Task Saving_a_student_without_a_pattern_is_refused()
    {
        var dto = new Savedata { CourseId = _course, SemesterId = "Sem-1", AYID = _ay, FirstName = "Asha", LastName = "Patil", MotherName = "Sunita", StudentType = "E", Gender = "Female", StudentPRN = "9" };

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
            FirstName = "A", LastName = "B", MotherName = "M", StudentType = "E", Gender = "Male", StudentPRN = prn,
        });
    [Fact]
    public async Task Ids_are_year_digits_and_a_running_five_digit_number()
    {
        Assert.Equal("2600001", await Save(_course, "1"));
        Assert.Equal("2600002", await Save(_course, "2"));
    }

    [Fact]
    public async Task Branches_share_one_college_counter()
    {
        Assert.Equal("2600001", await Save(_course, "1"));
        Assert.Equal("2600002", await Save(Branch("ECE"), "2"));
        Assert.Equal("2600003", await Save(_course, "3"));
    }

    [Fact]
    public async Task Numbering_continues_after_existing_and_deleted_students()
    {
        _context.StudentMasters.Add(new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "2600007", FirstName = "Old", LastName = "Gone", IsDeleted = true, CollegeId = _college });
        _context.StudentMasters.Add(new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "2600004", FirstName = "Old", LastName = "Here", CollegeId = _college });
        _context.StudentMasters.Add(new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "2600099", FirstName = "Other", LastName = "College", CollegeId = Guid.NewGuid() });
        _context.StudentMasters.Add(new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "26IL0050", FirstName = "Old", LastName = "Style", CollegeId = _college });
        await _context.SaveChangesAsync();

        Assert.Equal("2600008", await Save(_course, "1"));
    }

    [Fact]
    public async Task An_import_continues_the_same_counter_in_row_order()
    {
        Assert.Equal("2600001", await Save(_course, "1"));

        var file = await FilledTemplate(["Asha", "", "Patil", "Sunita", "", "", "Female", "E"], ["Ravi", "", "Shah", "Sunita", "", "", "Male", "E"]);
        Assert.True(Success(await _service.ImportStudentsAsync(Import(file))));

        Assert.Equal("Asha", (await _context.StudentMasters.SingleAsync(s => s.StudentId == "2600002")).FirstName);
        Assert.Equal("Ravi", (await _context.StudentMasters.SingleAsync(s => s.StudentId == "2600003")).FirstName);
        Assert.Equal("2600004", await Save(_course, "4"));
    }

    [Fact]
    public async Task A_new_academic_year_starts_its_own_counter()
    {
        var ay2 = Guid.NewGuid();
        _context.AcademicYears.Add(new AcademicYear { AYID = ay2, FullDuration = "2027-2028", ShortDuration = "2027-28", CollegeId = _college });
        await _context.SaveChangesAsync();

        Assert.Equal("2600001", await Save(_course, "1"));
        var id = await _service.SaveStudentAsync(new Savedata
        {
            CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = ay2,
            FirstName = "A", LastName = "B", MotherName = "M", StudentType = "E", Gender = "Male", StudentPRN = "2",
        });
        Assert.Equal("2700001", id);
    }

    [Fact]
    public async Task Several_students_without_an_ERN_can_be_saved_but_a_typed_ERN_is_still_unique()
    {
        Assert.Equal("2600001", await Save(_course, null!));
        Assert.Equal("2600002", await Save(_course, null!));
        await Save(_course, "77");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Save(_course, "77"));
        Assert.Contains("ERN already exists", ex.Message);
    }

    [Fact]
    public async Task Saving_without_a_branch_is_refused()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Save(Guid.Empty, "1"));
        Assert.Contains("branch", ex.Message);
    }
}
