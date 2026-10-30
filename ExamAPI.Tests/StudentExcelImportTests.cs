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
        {
            // Email (column 9) is required: a row given without one gets a unique address.
            var row = rows[r].Length == 8 ? [.. rows[r], $"student{r + 3}@mail.com"] : rows[r];
            for (int c = 0; c < row.Length; c++)
                sheet.Cell(r + 3, c + 1).Value = row[c];
        }
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
    public async Task Template_has_the_MotherName_column_after_LastName_and_Email_last()
    {
        var (bytes, _) = await _service.GenerateExcelTemplateAsync(new StudExcelDto { CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay });
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["FirstName", "MiddleName", "LastName", "MotherName", "Category", "ERN", "Gender", "StudentType", "Email"],
            Enumerable.Range(1, 9).Select(c => workbook.Worksheet(1).Cell(2, c).GetString()));
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
        var file = RawFile(["gender", " ERN ", "MotherName", "LastName", "FirstName", "email", "Category", "MiddleName", "studenttype"],
            ["Female", "777", "Sunita", "Patil", "Asha", "asha@mail.com", "obc", "R", "m"]);

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
            FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "  Sunita ", StudentType = "E", Email = "asha@mail.com",
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
        var dto = new Savedata { CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay, FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "Sunita", StudentType = " ", Email = "asha@mail.com" };
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
        var dto = new Savedata { CourseId = _course, SemesterId = "Sem-1", AYID = _ay, FirstName = "Asha", LastName = "Patil", MotherName = "Sunita", StudentType = "E", Gender = "Female", StudentPRN = "9", Email = "asha@mail.com" };

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
            FirstName = "A", LastName = "B", MotherName = "M", StudentType = "E", Gender = "Male", StudentPRN = prn, Email = $"s{prn}@mail.com",
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
            FirstName = "A", LastName = "B", MotherName = "M", StudentType = "E", Gender = "Male", StudentPRN = "2", Email = "s2@mail.com",
        });
        Assert.Equal("2700001", id);
    }

    [Fact]
    public async Task Several_students_without_an_ERN_can_be_saved_but_a_typed_ERN_is_still_unique()
    {
        Assert.Equal("2600001", await Save(_course, null!));
        Assert.Equal("2600002", await _service.SaveStudentAsync(new Savedata
        {
            CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay,
            FirstName = "C", LastName = "D", MotherName = "M", StudentType = "E", Gender = "Male", StudentPRN = null, Email = "c@mail.com",
        }));
        await Save(_course, "77");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(new Savedata
        {
            CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay,
            FirstName = "E", LastName = "F", MotherName = "M", StudentType = "E", Gender = "Male", StudentPRN = "77", Email = "e@mail.com",
        }));
        Assert.Contains("ERN already exists", ex.Message);
    }

    [Fact]
    public async Task Saving_without_a_branch_is_refused()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Save(Guid.Empty, "1"));
        Assert.Contains("branch", ex.Message);
    }

    // ---- Optional fields students can also fill in through their link: ABC ID, date of birth, phone, address ----

    private const string Email9 = "a@mail.com";

    private static IFormFile WithoutColumns(IFormFile file, int firstColumn, int lastColumn)
    {
        using var workbook = new XLWorkbook(file.OpenReadStream());
        workbook.Worksheet(1).Columns(firstColumn, lastColumn).Delete();
        var output = new MemoryStream();
        workbook.SaveAs(output);
        output.Position = 0;
        return new FormFile(output, 0, output.Length, "File", "students.xlsx");
    }

    [Fact]
    public async Task Template_has_the_optional_columns_after_Email()
    {
        var (bytes, _) = await _service.GenerateExcelTemplateAsync(new StudExcelDto { CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay });
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        Assert.Equal(["Email", "AbcId", "DateOfBirth", "Phone", "Address"], Enumerable.Range(9, 5).Select(c => sheet.Cell(2, c).GetString()));
    }

    [Fact]
    public async Task New_columns_are_imported_and_cleaned()
    {
        var file = await FilledTemplate(
            ["Asha", "", "Patil", "Sunita", "", "", "Female", "E", "asha@mail.com", "1234-5678 9012", "25-03-2005", "+91 98765 43210", "  Flat 4,  Shivaji Road,\nPune "],
            ["Ravi", "", "Shah", "Sunita", "", "", "Male", "E", "ravi@mail.com", "", "", "", ""]);

        var result = await _service.ImportStudentsAsync(Import(file));

        Assert.True(Success(result), string.Join("; ", Errors(result)));
        var asha = await _context.StudentMasters.SingleAsync(s => s.FirstName == "Asha");
        Assert.Equal("123456789012", asha.AbcId);
        Assert.Equal(new DateTime(2005, 3, 25), asha.DateOfBirth);
        Assert.Equal("9876543210", asha.Phone);
        Assert.Equal("Flat 4, Shivaji Road, Pune", asha.Address);
        var ravi = await _context.StudentMasters.SingleAsync(s => s.FirstName == "Ravi");
        Assert.Null(ravi.AbcId);
        Assert.Null(ravi.DateOfBirth);
        Assert.Null(ravi.Phone);
        Assert.Null(ravi.Address);
    }

    [Fact]
    public async Task A_real_Excel_date_is_accepted_for_the_date_of_birth()
    {
        var file = await FilledTemplate(["Asha", "", "Patil", "Sunita", "", "", "Female", "E", Email9, "", "", "", ""]);
        using (var workbook = new XLWorkbook(file.OpenReadStream()))
        {
            workbook.Worksheet(1).Cell(3, 11).Value = new DateTime(2004, 7, 9);
            var output = new MemoryStream();
            workbook.SaveAs(output);
            output.Position = 0;
            file = new FormFile(output, 0, output.Length, "File", "students.xlsx");
        }

        var result = await _service.ImportStudentsAsync(Import(file));

        Assert.True(Success(result), string.Join("; ", Errors(result)));
        Assert.Equal(new DateTime(2004, 7, 9), (await _context.StudentMasters.SingleAsync()).DateOfBirth);
    }

    [Fact]
    public async Task A_template_without_the_new_columns_still_imports()
    {
        var file = WithoutColumns(await FilledTemplate(["Asha", "", "Patil", "Sunita", "", "", "Female", "E", Email9, "", "", "", ""]), 10, 13);

        var result = await _service.ImportStudentsAsync(Import(file));

        Assert.True(Success(result), string.Join("; ", Errors(result)));
        var student = await _context.StudentMasters.SingleAsync();
        Assert.Null(student.AbcId);
        Assert.Null(student.DateOfBirth);
        Assert.Null(student.Phone);
        Assert.Null(student.Address);
    }

    [Fact]
    public async Task Bad_optional_values_are_refused_per_row_and_nothing_is_imported()
    {
        _context.StudentMasters.Add(new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = "OLD1", FirstName = "Old", LastName = "One", AbcId = "111111111111", CollegeId = _college });
        await _context.SaveChangesAsync();
        var tomorrow = DateTime.Today.AddDays(1).ToString("dd-MM-yyyy");
        var file = await FilledTemplate(
            ["A", "", "A", "M", "", "", "Male", "E", "a1@mail.com", "111111111111", "", "", ""],              // row 3: ABC ID already used
            ["B", "", "B", "M", "", "", "Male", "E", "a2@mail.com", "222222222222", "", "", ""],              // row 4: fine
            ["C", "", "C", "M", "", "", "Male", "E", "a3@mail.com", "2222 2222 2222", "", "", ""],            // row 5: ABC ID twice in file
            ["D", "", "D", "M", "", "", "Male", "E", "a4@mail.com", "12345", "", "", ""],                     // row 6: ABC ID too short
            ["E", "", "E", "M", "", "", "Male", "E", "a5@mail.com", "", "31-02-2005", "", ""],                // row 7: not a date
            ["F", "", "F", "M", "", "", "Male", "E", "a6@mail.com", "", tomorrow, "", ""],                    // row 8: future
            ["G", "", "G", "M", "", "", "Male", "E", "a7@mail.com", "", "01-01-1900", "", ""],               // row 9: too old
            ["H", "", "H", "M", "", "", "Male", "E", "a8@mail.com", "", "", "12345", ""],                     // row 10: phone
            ["I", "", "I", "M", "", "", "Male", "E", "a9@mail.com", "", "", "5876543210", ""],                // row 11: phone starts below 6
            ["J", "", "J", "M", "", "", "Male", "E", "b1@mail.com", "", "", "", new string('x', 301)],        // row 12: address too long
            ["K", "", "K", "M", "", "", "Male", "E", "b2@mail.com", "", "25-03-2005", "9876543210", "Pune"]); // row 13: fine

        var result = await _service.ImportStudentsAsync(Import(file));

        Assert.False(Success(result));
        var errors = Errors(result);
        Assert.Contains(errors, e => e.StartsWith("Row 3:") && e.Contains("ABC ID 111111111111") && e.Contains("already belongs"));
        Assert.DoesNotContain(errors, e => e.StartsWith("Row 4:"));
        Assert.Contains(errors, e => e.StartsWith("Row 5:") && e.Contains("ABC ID 222222222222") && e.Contains("twice"));
        Assert.Contains(errors, e => e.StartsWith("Row 6:") && e.Contains("12-digit"));
        Assert.Contains(errors, e => e.StartsWith("Row 7:") && e.Contains("dd-MM-yyyy"));
        Assert.Contains(errors, e => e.StartsWith("Row 8:") && e.Contains("future"));
        Assert.Contains(errors, e => e.StartsWith("Row 9:") && e.Contains("check the year"));
        Assert.Contains(errors, e => e.StartsWith("Row 10:") && e.Contains("10-digit"));
        Assert.Contains(errors, e => e.StartsWith("Row 11:") && e.Contains("10-digit"));
        Assert.Contains(errors, e => e.StartsWith("Row 12:") && e.Contains("300"));
        Assert.DoesNotContain(errors, e => e.StartsWith("Row 13:"));
        Assert.Equal(1, await _context.StudentMasters.CountAsync()); // nothing imported
    }

    private Savedata NewStudent(string email, Action<Savedata>? tweak = null)
    {
        var dto = new Savedata
        {
            CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay,
            FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "Sunita", StudentType = "E", Email = email,
        };
        tweak?.Invoke(dto);
        return dto;
    }

    private Savedata Edit(string id, Action<Savedata>? tweak = null)
    {
        var dto = new Savedata
        {
            StudentId = id, CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay,
            FirstName = "Asha", LastName = "Patil", Gender = "Female", MotherName = "Sunita", StudentType = "E",
        };
        tweak?.Invoke(dto);
        return dto;
    }

    [Fact]
    public async Task Saving_with_ABC_ID_date_of_birth_phone_and_address_stores_and_returns_them()
    {
        var id = await _service.SaveStudentAsync(NewStudent("asha@mail.com", d =>
        {
            d.AbcId = "1234 5678 9012";
            d.DateOfBirth = new DateTime(2005, 3, 25, 14, 30, 0);
            d.Phone = "098765-43210";
            d.Address = "  Flat 4,   Pune ";
        }));

        var saved = await _service.GetStudentByIdAsync(id, _ay);
        Assert.Equal("123456789012", saved.AbcId);
        Assert.Equal(new DateTime(2005, 3, 25), saved.DateOfBirth);
        Assert.Equal("9876543210", saved.Phone);
        Assert.Equal("Flat 4, Pune", saved.Address);
    }

    [Fact]
    public void The_date_input_value_and_null_blanks_from_the_form_bind_to_the_dto()
    {
        var json = """{"firstName":"A","dateOfBirth":"2005-03-25","phone":null,"address":null,"abcId":null}""";
        var dto = System.Text.Json.JsonSerializer.Deserialize<Savedata>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(new DateTime(2005, 3, 25), dto.DateOfBirth);
        Assert.Null(dto.Phone);
    }

    [Fact]
    public async Task Saving_with_these_left_blank_is_fine_and_stores_nothing()
    {
        var id = await _service.SaveStudentAsync(NewStudent("asha@mail.com", d => { d.AbcId = " "; d.Phone = ""; d.Address = "  "; d.DateOfBirth = null; }));

        var saved = await _service.GetStudentByIdAsync(id, _ay);
        Assert.Null(saved.AbcId);
        Assert.Null(saved.DateOfBirth);
        Assert.Null(saved.Phone);
        Assert.Null(saved.Address);
    }

    [Fact]
    public async Task Saving_with_a_bad_phone_date_address_or_a_taken_ABC_ID_is_refused()
    {
        await _service.SaveStudentAsync(NewStudent("first@mail.com", d => d.AbcId = "111111111111"));

        var phone = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(NewStudent("a@mail.com", d => d.Phone = "12345")));
        Assert.Contains("10-digit", phone.Message);
        var future = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(NewStudent("b@mail.com", d => d.DateOfBirth = DateTime.Today.AddDays(1))));
        Assert.Contains("future", future.Message);
        var old = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(NewStudent("c@mail.com", d => d.DateOfBirth = new DateTime(1900, 1, 1))));
        Assert.Contains("check the year", old.Message);
        var young = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(NewStudent("d@mail.com", d => d.DateOfBirth = DateTime.Today.AddYears(-5))));
        Assert.Contains("check the year", young.Message);
        var address = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(NewStudent("e@mail.com", d => d.Address = new string('x', 301))));
        Assert.Contains("300", address.Message);
        var short12 = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(NewStudent("f@mail.com", d => d.AbcId = "123")));
        Assert.Contains("12-digit", short12.Message);
        var taken = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(NewStudent("g@mail.com", d => d.AbcId = "1111-1111-1111")));
        Assert.Contains("belongs to another student", taken.Message);

        Assert.Equal(1, await _context.StudentMasters.CountAsync()); // only the first one was saved
    }

    [Fact]
    public async Task Editing_changes_clears_and_checks_the_optional_fields()
    {
        var id = await _service.SaveStudentAsync(NewStudent("asha@mail.com", d => { d.Phone = "9876543210"; d.Address = "Pune"; d.DateOfBirth = new DateTime(2005, 3, 25); d.AbcId = "123456789012"; }));
        var other = await _service.SaveStudentAsync(NewStudent("ravi@mail.com", d => { d.FirstName = "Ravi"; d.AbcId = "999999999999"; }));

        await _service.UpdateStudentAsync(Edit(id, d => { d.Email = "asha@mail.com"; d.Phone = "8888888888"; d.Address = "Mumbai"; d.DateOfBirth = new DateTime(2004, 1, 2); d.AbcId = "123456789012"; }));
        var changed = await _service.GetStudentByIdAsync(id, _ay);
        Assert.Equal("8888888888", changed.Phone);
        Assert.Equal("Mumbai", changed.Address);
        Assert.Equal(new DateTime(2004, 1, 2), changed.DateOfBirth);

        var bad = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateStudentAsync(Edit(id, d => { d.Email = "asha@mail.com"; d.Phone = "123"; })));
        Assert.Contains("10-digit", bad.Message);
        var taken = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateStudentAsync(Edit(id, d => { d.Email = "asha@mail.com"; d.AbcId = "999999999999"; })));
        Assert.Contains("belongs to another student", taken.Message);
        Assert.Equal("8888888888", (await _service.GetStudentByIdAsync(id, _ay)).Phone); // the refused edits changed nothing
        Assert.Equal("999999999999", (await _service.GetStudentByIdAsync(other, _ay)).AbcId);

        await _service.UpdateStudentAsync(Edit(id, d => d.Email = "asha@mail.com")); // all four blank: cleared
        var cleared = await _service.GetStudentByIdAsync(id, _ay);
        Assert.Null(cleared.Phone);
        Assert.Null(cleared.Address);
        Assert.Null(cleared.DateOfBirth);
        Assert.Null(cleared.AbcId);
    }

    [Fact]
    public async Task A_value_already_on_record_is_kept_when_another_field_is_edited()
    {
        // An older record whose values would fail today's checks.
        var student = new StudentMaster
        {
            StdMstId = Guid.NewGuid(), StudentId = "2600001", FirstName = "Old", LastName = "Record", MotherName = "M", StudentType = "E", CollegeId = _college,
            DateOfBirth = new DateTime(1930, 1, 1), Phone = "12345", Address = new string('y', 400),
        };
        _context.StudentMasters.Add(student);
        _context.StudentEligibilities.Add(new StudentEligibility { Id = Guid.NewGuid(), StdMstId = student.StdMstId, StudentId = "2600001", CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay });
        await _context.SaveChangesAsync();

        await _service.UpdateStudentAsync(Edit("2600001", d => { d.FirstName = "Renamed"; d.DateOfBirth = new DateTime(1930, 1, 1); d.Phone = "12345"; d.Address = new string('y', 400); }));

        var saved = await _context.StudentMasters.SingleAsync();
        Assert.Equal("Renamed", saved.FirstName);
        Assert.Equal("12345", saved.Phone);
        Assert.Equal(400, saved.Address!.Length);
    }

    [Fact]
    public async Task The_student_lists_show_ABC_ID_and_phone()
    {
        await _service.SaveStudentAsync(NewStudent("asha@mail.com", d => { d.AbcId = "123456789012"; d.Phone = "9876543210"; }));

        var byCourse = await _service.GetbycourseAsync(_course, _ay);
        Assert.Equal("123456789012", byCourse.Single().AbcId);
        Assert.Equal("9876543210", byCourse.Single().Phone);
        var found = await _service.SearchStudentsAsync(new Searchbyname { FirstName = "Asha", StudentPRN = "", StudentId = "", AYID = _ay }, _ay);
        Assert.Equal("123456789012", found.Single().AbcId);
        Assert.Equal("9876543210", found.Single().Phone);
    }
}
