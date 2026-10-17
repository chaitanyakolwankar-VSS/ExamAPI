using ClosedXML.Excel;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Files;
using ExamAPI.Services.StudentAccess;
using ExamAPI.Services.StudentMasters;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>
/// Student access, step 2: emails on Student Master. New students need one (import and single form); students
/// saved earlier get theirs from the email sheet, matched by Student ID. Bad or repeated emails stop the save;
/// a domain outside the college's list is saved but Flagged.
/// </summary>
public sealed class StudentEmailImportTests
{
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _course = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly StudentMasterService _service;

    public StudentEmailImportTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _context.CourseMasters.Add(new CourseMaster { CourseId = _course, Name = "Civil Engineering", CourseCode = "CIVIL", CollegeId = _college });
        _context.AcademicYears.Add(new AcademicYear { AYID = _ay, FullDuration = "2026-2027", ShortDuration = "2026-27", IsCurrent = true, CollegeId = _college });
        _context.SaveChanges();
        _service = new StudentMasterService(_context, new Mock<IFileStorage>().Object);
    }

    private StudExcelDto Class() => new() { CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay };
    private StudentImportDto Upload(IFormFile file, bool preview = false) => new() { File = file, CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay, Preview = preview };

    private static IFormFile AsFile(XLWorkbook workbook)
    {
        var output = new MemoryStream();
        workbook.SaveAs(output);
        output.Position = 0;
        return new FormFile(output, 0, output.Length, "File", "sheet.xlsx");
    }

    /// <summary>The downloaded template with rows of (first name, email).</summary>
    private async Task<IFormFile> Template(params (string First, string Email)[] rows)
    {
        var (bytes, _) = await _service.GenerateExcelTemplateAsync(Class());
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        for (int r = 0; r < rows.Length; r++)
        {
            string[] values = [rows[r].First, "", "Patil", "Sunita", "", "", "Female", "E", rows[r].Email];
            for (int c = 0; c < values.Length; c++) sheet.Cell(r + 3, c + 1).Value = values[c];
        }
        return AsFile(workbook);
    }

    private StudentMaster Saved(string id, string first, string? email = null)
    {
        var student = new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = id, FirstName = first, LastName = "Shah", MotherName = "Meena", Email = email, CollegeId = _college };
        _context.StudentMasters.Add(student);
        _context.StudentEligibilities.Add(new StudentEligibility { Id = Guid.NewGuid(), StdMstId = student.StdMstId, StudentId = id, CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay, CollegeId = _college });
        _context.SaveChanges();
        return student;
    }

    private void AllowDomains(string domains)
    {
        _context.CollegeSettings.Add(new CollegeSettings { Id = Guid.NewGuid(), CollegeId = _college, AllowedEmailDomains = domains });
        _context.SaveChanges();
    }

    // ------------------------------------------------------------------ rules

    [Theory]
    [InlineData("  Ravi.Patil@Mail.COM ", "ravi.patil@mail.com", true)]
    [InlineData("ravi@mail", "ravi@mail", false)]
    [InlineData("ravi mail@x.com", "ravi mail@x.com", false)]
    [InlineData("ravi@@x.com", "ravi@@x.com", false)]
    [InlineData("ravi..p@x.com", "ravi..p@x.com", false)]
    [InlineData("ravi@x.co.in", "ravi@x.co.in", true)]
    public void Emails_are_normalized_and_checked(string input, string normalized, bool valid)
    {
        Assert.Equal(normalized, StudentEmail.Normalize(input));
        Assert.Equal(valid, StudentEmail.IsValid(normalized));
    }

    [Fact]
    public void A_domain_outside_the_list_is_flagged_and_an_empty_list_accepts_any()
    {
        Assert.Null(StudentEmail.FlagReason("a@gmail.com", StudentEmail.ParseDomains("")));
        Assert.Null(StudentEmail.FlagReason("a@viva.in", StudentEmail.ParseDomains("@Viva.in, student.viva.in")));
        Assert.Contains("gmail.com", StudentEmail.FlagReason("a@gmail.com", StudentEmail.ParseDomains("viva.in")));
    }

    // ------------------------------------------------------------------ new-student import

    [Fact]
    public async Task Import_needs_a_valid_email_once_per_file_and_not_already_used()
    {
        Saved("2600001", "Old", "taken@mail.com");
        var file = await Template(("Asha", ""), ("Ravi", "not-an-email"), ("Mina", "same@mail.com"), ("Gita", "SAME@mail.com"), ("Tara", "taken@mail.com"), ("Ok", "ok@mail.com"));

        var result = await _service.ImportStudentsAsync(Upload(file));

        Assert.False(result.Success);
        Assert.Contains("Row 3: email is required.", result.Errors);
        Assert.Contains(result.Errors, e => e.StartsWith("Row 4:") && e.Contains("not a valid email"));
        Assert.Contains(result.Errors, e => e.StartsWith("Row 6:") && e.Contains("twice"));
        Assert.Contains(result.Errors, e => e.StartsWith("Row 7:") && e.Contains("already belongs to student 2600001"));
        Assert.DoesNotContain(result.Errors, e => e.StartsWith("Row 5:") || e.StartsWith("Row 8:"));
        Assert.Equal(ImportRowStatuses.Ok, result.Rows.Single(r => r.Row == 8).Status);
        Assert.Equal(ImportRowStatuses.Error, result.Rows.Single(r => r.Row == 3).Status);
        Assert.Single(_context.StudentMasters); // nothing imported
    }

    [Fact]
    public async Task Preview_answers_per_row_and_saves_nothing_then_save_stores_emails_lower_case()
    {
        AllowDomains("viva.in");
        var file = await Template(("Asha", " Asha@Viva.in "), ("Ravi", "ravi@gmail.com"));

        var preview = await _service.ImportStudentsAsync(Upload(file, preview: true));

        Assert.True(preview.Success);
        Assert.False(preview.Saved);
        Assert.Equal(new[] { ImportRowStatuses.Ok, ImportRowStatuses.Flagged }, preview.Rows.Select(r => r.Status));
        Assert.Contains("gmail.com", preview.Rows[1].Note);
        Assert.Empty(_context.StudentMasters);

        var saved = await _service.ImportStudentsAsync(Upload(await Template(("Asha", " Asha@Viva.in "), ("Ravi", "ravi@gmail.com"))));

        Assert.True(saved.Saved);
        Assert.Equal(new[] { "2600001", "2600002" }, saved.Rows.Select(r => r.StudentId));
        var students = await _context.StudentMasters.OrderBy(s => s.StudentId).ToListAsync();
        Assert.Equal(("asha@viva.in", null), (students[0].Email, students[0].EmailStatus));
        Assert.Equal(("ravi@gmail.com", EmailVerification.Flagged), (students[1].Email, students[1].EmailStatus));
        Assert.NotNull(students[1].EmailFlagReason);
    }

    [Fact]
    public async Task A_template_from_before_email_is_refused_with_a_clear_message()
    {
        var (bytes, _) = await _service.GenerateExcelTemplateAsync(Class());
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheet(1).Cell(2, 9).Value = ""; // old 8-column template
        workbook.Worksheet(1).Cell(3, 1).Value = "Asha";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ImportStudentsAsync(Upload(AsFile(workbook))));
        Assert.Contains("no Email column", ex.Message);
    }

    // ------------------------------------------------------------------ single student

    private Savedata NewStudent(string? email) => new()
    {
        CourseId = _course, SemesterId = "Sem-1", Pattern = "NEP", AYID = _ay,
        FirstName = "Asha", LastName = "Patil", MotherName = "Sunita", Gender = "Female", StudentType = "E", Email = email,
    };

    [Fact]
    public async Task A_new_student_needs_a_valid_unused_email()
    {
        Saved("2600001", "Old", "taken@mail.com"); // no ERN, like the new student: that is no clash

        Assert.Equal("Email is required.", (await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(NewStudent(" ")))).Message);
        Assert.Contains("not a valid email", (await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(NewStudent("x@y")))).Message);
        Assert.Contains("already belongs to student 2600001", (await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveStudentAsync(NewStudent("TAKEN@mail.com")))).Message);

        var id = await _service.SaveStudentAsync(NewStudent(" Asha@Mail.com "));
        Assert.Equal("asha@mail.com", (await _service.GetStudentByIdAsync(id, _ay)).Email);
    }

    [Fact]
    public async Task Editing_a_student_can_add_or_change_the_email_and_records_it()
    {
        var old = Saved("2600001", "Old");
        var edit = NewStudent(null);
        edit.StudentId = "2600001";

        await _service.UpdateStudentAsync(edit); // no email yet: still fine
        Assert.Null((await _context.StudentMasters.SingleAsync()).Email);

        edit.Email = "Old@Mail.com";
        await _service.UpdateStudentAsync(edit);

        Assert.Equal("old@mail.com", (await _context.StudentMasters.SingleAsync()).Email);
        var audit = await _context.AuditLogs.SingleAsync(a => a.Event == "EmailSet");
        Assert.Equal(old.StdMstId.ToString(), audit.RecordId);
        Assert.Contains("(none) -> old@mail.com", audit.Details);
        Assert.Equal(_college, audit.CollegeId);
    }

    // ------------------------------------------------------------------ email sheet

    private async Task<XLWorkbook> EmailSheet()
    {
        var (bytes, name) = await _service.GenerateEmailSheetAsync(Class());
        Assert.StartsWith("Student emails Civil Engineering Sem-1 NEP", name);
        return new XLWorkbook(new MemoryStream(bytes));
    }

    private static int RowOf(IXLWorksheet sheet, string studentId) =>
        Enumerable.Range(3, 100).First(r => sheet.Cell(r, 1).GetString() == studentId);

    [Fact]
    public async Task The_email_sheet_lists_the_class_by_name_with_only_the_email_column_editable()
    {
        Saved("2600002", "Zara", "zara@mail.com");
        Saved("2600001", "Asha");

        using var workbook = await EmailSheet();
        var sheet = workbook.Worksheet(1);

        Assert.Equal(new[] { "StudentId", "Name", "ERN", "Email" }, Enumerable.Range(1, 4).Select(c => sheet.Cell(2, c).GetString()));
        Assert.Equal(new[] { "2600001", "2600002" }, new[] { sheet.Cell(3, 1).GetString(), sheet.Cell(4, 1).GetString() }); // SHAH ASHA before SHAH ZARA
        Assert.Equal("zara@mail.com", sheet.Cell(4, 4).GetString());
        Assert.True(sheet.IsProtected);
        Assert.True(workbook.IsProtected);
        Assert.False(sheet.Cell(3, 4).Style.Protection.Locked);
        Assert.True(sheet.Cell(3, 1).Style.Protection.Locked);
        Assert.True(sheet.Cell(3, 2).Style.Protection.Locked);
    }

    [Fact]
    public async Task The_filled_sheet_sets_emails_by_student_id_and_leaves_blank_and_same_rows_alone()
    {
        var asha = Saved("2600001", "Asha");
        Saved("2600002", "Ravi", "ravi@mail.com");
        Saved("2600003", "Mina");

        using var workbook = await EmailSheet();
        var sheet = workbook.Worksheet(1);
        sheet.Cell(RowOf(sheet, "2600001"), 4).Value = " Asha@Mail.com ";
        // Ravi keeps the same email; Mina stays blank.

        var preview = await _service.ImportEmailsAsync(Upload(AsFile(workbook), preview: true));
        Assert.True(preview.Success);
        Assert.False(preview.Saved);
        Assert.Null((await _context.StudentMasters.SingleAsync(s => s.StudentId == "2600001")).Email);
        Assert.Equal(ImportRowStatuses.Ok, preview.Rows.Single(r => r.StudentId == "2600001").Status);
        Assert.Equal(ImportRowStatuses.Unchanged, preview.Rows.Single(r => r.StudentId == "2600002").Status);
        Assert.Equal(ImportRowStatuses.Skipped, preview.Rows.Single(r => r.StudentId == "2600003").Status);

        var saved = await _service.ImportEmailsAsync(Upload(AsFile(workbook)));

        Assert.True(saved.Saved);
        Assert.Equal("1 email(s) saved.", saved.Message);
        Assert.Equal("asha@mail.com", (await _context.StudentMasters.SingleAsync(s => s.StudentId == "2600001")).Email);
        Assert.Null((await _context.StudentMasters.SingleAsync(s => s.StudentId == "2600003")).Email);
        Assert.Equal(3, await _context.StudentMasters.CountAsync()); // nothing new is created
        var audit = await _context.AuditLogs.SingleAsync(a => a.Event == "EmailSet");
        Assert.Equal(asha.StdMstId.ToString(), audit.RecordId);
    }

    [Fact]
    public async Task A_wrong_student_or_a_repeated_or_taken_email_stops_the_whole_sheet()
    {
        Saved("2600001", "Asha");
        Saved("2600002", "Ravi");
        Saved("2600003", "Mina", "mina@mail.com");
        Saved("2600004", "Gita");

        using var workbook = await EmailSheet();
        var sheet = workbook.Worksheet(1);
        sheet.Cell(RowOf(sheet, "2600001"), 4).Value = "same@mail.com";
        sheet.Cell(RowOf(sheet, "2600002"), 4).Value = "SAME@mail.com";
        sheet.Cell(RowOf(sheet, "2600004"), 4).Value = "mina@mail.com";
        sheet.Cell(20, 1).Value = "9999999"; // not in this class
        sheet.Cell(20, 4).Value = "x@mail.com";

        var result = await _service.ImportEmailsAsync(Upload(AsFile(workbook)));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("same@mail.com appears twice"));
        Assert.Contains(result.Errors, e => e.Contains("already belongs to student 2600003"));
        Assert.Contains(result.Errors, e => e.StartsWith("Row 20:") && e.Contains("not in this branch"));
        Assert.All(await _context.StudentMasters.Where(s => s.StudentId != "2600003").ToListAsync(), s => Assert.Null(s.Email));
    }

    [Fact]
    public async Task A_student_template_uploaded_as_the_email_sheet_is_refused()
    {
        var (bytes, _) = await _service.GenerateExcelTemplateAsync(Class());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ImportEmailsAsync(Upload(new FormFile(new MemoryStream(bytes), 0, bytes.Length, "File", "t.xlsx"))));
        Assert.Contains("not the email sheet", ex.Message);
    }
}
