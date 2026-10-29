using ClosedXML.Excel;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Staff;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace ExamAPI.Tests;

/// <summary>Staff Master: the Excel sheet adds and updates staff, the form and the sheet share the same checks, lists stay clean.</summary>
public sealed class StaffServiceTests
{
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly StaffService _service;

    // EmployeeId, Title, FirstName, MiddleName, LastName, Email, Department, Designation, Teaching, External, ExternalCollege, ExternalCity
    private static readonly string[] Asha = { "E101", "Prof.", "Asha", "R.", "Patil", "asha@viva.edu", "Computer", "Assistant Professor", "", "No", "", "" };
    private static readonly string[] Ravi = { "e102", "dr", "Ravi", "", "Kumar", "RAVI@viva.edu", "Computer", "Professor", "", "", "", "" };
    private static readonly string[] Meena = { "", "Dr.", "Meena", "", "Shah", "meena@other.edu", "", "", "", "Yes", "Other College of Engineering", "Pune" };

    public StaffServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _context.Colleges.Add(new College { CollegeId = _college, Name = "VIVA Institute of Technology", CollegeCode = "VIT", CollegeCenter = "Virar", ContactEmail = "office@viva.edu", ContactPhone = "0250" });
        _context.SaveChanges();
        _service = new StaffService(_context);
    }

    /// <summary>The downloaded sheet with its data rows replaced by the given ones.</summary>
    private async Task<IFormFile> Sheet(params string[][] rows)
    {
        var (bytes, _) = await _service.TemplateAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        sheet.Range(3, 1, 1002, StaffService.Headers.Length).Clear(XLClearOptions.Contents);
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                sheet.Cell(r + 3, c + 1).Value = rows[r][c];
        var output = new MemoryStream();
        workbook.SaveAs(output);
        output.Position = 0;
        return new FormFile(output, 0, output.Length, "File", "staff.xlsx");
    }

    private async Task<StaffImportResult> Import(bool preview, params string[][] rows)
        => await _service.ImportAsync(new StaffImportRequest { File = await Sheet(rows), Preview = preview });

    private static StaffSave Form(string code, string email, string first = "Kiran") => new()
    {
        EmployeeCode = code, Title = "Mr.", FirstName = first, LastName = "Rao", Email = email
    };

    [Fact]
    public async Task Preview_shows_what_changes_then_save_adds_staff_and_new_list_entries()
    {
        var preview = await Import(true, Asha, Ravi, Meena);

        Assert.True(preview.Success);
        Assert.False(preview.Saved);
        Assert.All(preview.Rows, r => Assert.Equal(StaffImportActions.New, r.Action));
        Assert.Equal(new[] { "Department: Computer", "Designation: Assistant Professor", "Designation: Professor" }, preview.NewOptions);
        Assert.Contains("new department \"Computer\" will be added", preview.Rows[0].Note, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_context.StaffMasters);

        var saved = await Import(false, Asha, Ravi, Meena);

        Assert.True(saved.Saved);
        Assert.Equal("Saved: 3 new, 0 updated, 0 unchanged.", saved.Message);
        var staff = await _service.ListAsync(new StaffFilter());
        Assert.Equal(new[] { "Prof. Asha R. Patil", "Dr. Ravi Kumar", "Dr. Meena Shah" }, staff.Select(s => s.Name));
        var ravi = staff[1];
        Assert.Equal("E102", ravi.EmployeeCode);           // tidied: upper case
        Assert.Equal("ravi@viva.edu", ravi.Email);         // lower case
        Assert.Equal("Computer", ravi.Department);
        Assert.Equal("Professor", ravi.Designation);
        var meena = staff[2];
        Assert.True(meena.IsExternal);
        Assert.Null(meena.EmployeeCode);
        Assert.Equal("Other College of Engineering", meena.ExternalCollege);
        Assert.Equal("Pune", meena.ExternalCity);
        Assert.Equal(3, (await _service.OptionsAsync()).Count);
    }

    [Fact]
    public async Task The_downloaded_sheet_lists_current_staff_and_reimporting_it_updates_by_employee_id_or_email()
    {
        await Import(false, Asha, Ravi, Meena);

        var (bytes, name) = await _service.TemplateAsync();
        Assert.Equal("Staff VIVA Institute of Technology", name);
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Staff");
            Assert.Equal("E101", sheet.Cell(3, 1).GetString());
            Assert.Equal("Yes", sheet.Cell(5, 10).GetString());
            Assert.Equal("Yes", sheet.Cell(3, 9).GetString());   // Teaching
            Assert.Equal(XLWorksheetVisibility.Hidden, workbook.Worksheet("Lists").Visibility);
            Assert.Equal("Computer", workbook.Worksheet("Lists").Cell(2, 2).GetString());
        }

        var renamed = (string[])Asha.Clone(); renamed[4] = "Patil-Desai"; renamed[5] = "asha.pd@viva.edu";
        var moved = (string[])Meena.Clone(); moved[11] = "Mumbai";
        var result = await Import(false, renamed, Ravi, moved);

        Assert.Equal(new[] { StaffImportActions.Update, StaffImportActions.Unchanged, StaffImportActions.Update }, result.Rows.Select(r => r.Action));
        var staff = await _service.ListAsync(new StaffFilter());
        Assert.Equal(3, staff.Count);
        Assert.Contains(staff, s => s.EmployeeCode == "E101" && s.LastName == "Patil-Desai" && s.Email == "asha.pd@viva.edu");
        Assert.Contains(staff, s => s.Email == "meena@other.edu" && s.ExternalCity == "Mumbai");
    }

    [Fact]
    public async Task Any_bad_row_stops_the_whole_import_with_every_problem_listed()
    {
        await Import(false, Asha);
        string[] noTitle = { "E201", "", "Sita", "", "Rao", "sita@viva.edu", "", "", "", "", "", "" };
        string[] badTitle = { "E202", "Sir", "Ram", "", "Rao", "ram@viva.edu", "", "", "", "", "", "" };
        string[] noCode = { "", "Mr.", "Amit", "", "Jain", "amit@viva.edu", "", "", "", "No", "", "" };
        string[] externalNoCollege = { "", "Dr.", "Neha", "", "Iyer", "neha@other.edu", "", "", "", "Yes", "", "" };
        string[] badExternal = { "E203", "Mr.", "Om", "", "Das", "om@viva.edu", "", "", "", "Maybe", "", "" };
        string[] takenEmail = { "E204", "Mr.", "Raj", "", "Sen", "asha@viva.edu", "", "", "", "", "", "" };
        string[] twiceA = { "E205", "Mr.", "Dev", "", "Roy", "dev@viva.edu", "", "", "", "", "", "" };
        string[] twiceB = { "E205", "Mr.", "Dev", "", "Roy", "dev@viva.edu", "", "", "", "", "", "" };
        string[] badEmail = { "E206", "Mr.", "Jay", "", "Pal", "jay-at-viva", "", "", "", "", "", "" };

        var result = await Import(false, noTitle, badTitle, noCode, externalNoCollege, badExternal, takenEmail, twiceA, twiceB, badEmail);

        Assert.False(result.Success);
        Assert.False(result.Saved);
        Assert.Contains(result.Errors, e => e.StartsWith("Row 3:") && e.Contains("title is required"));
        Assert.Contains(result.Errors, e => e.StartsWith("Row 4:") && e.Contains("title \"Sir\" is not in the list"));
        Assert.Contains(result.Errors, e => e.StartsWith("Row 5:") && e.Contains("employee ID is required"));
        Assert.Contains(result.Errors, e => e.StartsWith("Row 6:") && e.Contains("external staff need their college"));
        Assert.Contains(result.Errors, e => e.StartsWith("Row 7:") && e.Contains("External must be Yes or No"));
        Assert.Contains(result.Errors, e => e.StartsWith("Row 8:") && e.Contains("email asha@viva.edu already belongs to Prof. Asha R. Patil (E101)"));
        Assert.Contains(result.Errors, e => e.StartsWith("Row 10:") && e.Contains("employee ID E205 appears twice"));
        Assert.Contains(result.Errors, e => e.StartsWith("Row 10:") && e.Contains("email dev@viva.edu appears twice"));
        Assert.Contains(result.Errors, e => e.StartsWith("Row 11:") && e.Contains("not a valid email"));
        Assert.Single(_context.StaffMasters);
    }

    [Fact]
    public async Task The_form_refuses_a_taken_employee_id_or_email_and_another_college_never_clashes()
    {
        _context.StaffMasters.Add(new StaffMaster { StaffId = Guid.NewGuid(), CollegeId = _otherCollege, EmployeeCode = "E1", Title = "Mr.", FirstName = "Other", Email = "same@mail.com" });
        await _context.SaveChangesAsync();

        var first = await _service.SaveAsync(null, Form("E1", "same@mail.com"));      // other college's ID and email are fine here

        Assert.Equal("Mr. Kiran Rao", first.Name);
        var taken = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveAsync(null, Form("e1", "new@mail.com")));
        Assert.Equal("Employee ID E1 already belongs to someone else.", taken.Message);
        var email = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveAsync(null, Form("E2", "SAME@mail.com")));
        Assert.Equal("Email same@mail.com already belongs to someone else.", email.Message);
        Assert.Single(await _service.ListAsync(new StaffFilter()));

        var edited = await _service.SaveAsync(first.StaffId, Form("E1", "same@mail.com", first: "Kiran K."));
        Assert.Equal("Mr. Kiran K. Rao", edited.Name);
    }

    [Fact]
    public async Task Lists_refuse_duplicates_and_an_entry_in_use_cannot_be_deleted()
    {
        var computer = await _service.AddOptionAsync(new StaffOptionSave { Kind = "department", Name = "  Computer   Engineering " });
        var unused = await _service.AddOptionAsync(new StaffOptionSave { Kind = "Department", Name = "Civil" });
        Assert.Equal("Computer Engineering", computer.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddOptionAsync(new StaffOptionSave { Kind = "Department", Name = "Computer Engineering" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddOptionAsync(new StaffOptionSave { Kind = "Team", Name = "X" }));

        var form = Form("E1", "k@mail.com");
        form.DepartmentId = computer.Id;
        await _service.SaveAsync(null, form);

        var inUse = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteOptionAsync(computer.Id));
        Assert.Contains("used by 1 staff member", inUse.Message);
        await _service.DeleteOptionAsync(unused.Id);
        var renamed = await _service.RenameOptionAsync(computer.Id, new StaffOptionSave { Name = "Computer Engg." });
        Assert.Equal(1, renamed.StaffCount);
        Assert.Equal("Computer Engg.", (await _service.ListAsync(new StaffFilter())).Single().Department);
        Assert.Single(await _service.OptionsAsync());
    }

    [Fact]
    public async Task Staff_are_teaching_unless_the_sheet_or_the_form_says_otherwise()
    {
        string[] lab = { "E103", "Mr.", "Anil", "", "More", "anil@viva.edu", "Lab", "Lab Assistant", "No", "", "", "" };
        string[] office = { "E104", "Mrs.", "Neela", "", "Joshi", "neela@viva.edu", "", "", "n", "", "", "" };   // N counts as No

        var preview = await Import(true, Asha, lab, office);
        Assert.True(preview.Success);
        await Import(false, Asha, Ravi, lab, office);

        var staff = (await _service.ListAsync(new StaffFilter())).ToDictionary(s => s.FirstName);
        Assert.True(staff["Asha"].IsTeaching);      // blank means Yes
        Assert.True(staff["Ravi"].IsTeaching);
        Assert.False(staff["Anil"].IsTeaching);
        Assert.False(staff["Neela"].IsTeaching);

        // The downloaded sheet (by first name: Anil, Asha, Neela, Ravi) shows it, and importing it back changes nothing.
        var (bytes, _) = await _service.TemplateAsync();
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Staff");
            Assert.Equal("Teaching", sheet.Cell(2, 9).GetString());
            Assert.Equal(new[] { "No", "Yes", "No", "Yes" }, Enumerable.Range(3, 4).Select(r => sheet.Cell(r, 9).GetString()));
            Assert.Contains(sheet.DataValidations, v => v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == 9));   // the Yes / No drop-down
        }
        var same = await Import(true, Asha, Ravi, lab, office);
        Assert.All(same.Rows, r => Assert.Equal(StaffImportActions.Unchanged, r.Action));

        // Changing only the Teaching cell is an update.
        var teachesNow = (string[])lab.Clone(); teachesNow[8] = "Yes";
        var changed = await Import(false, Asha, Ravi, teachesNow, office);
        Assert.Equal(new[] { "Unchanged", "Unchanged", "Update", "Unchanged" }, changed.Rows.Select(r => r.Action));
        Assert.True((await _service.ListAsync(new StaffFilter())).Single(s => s.FirstName == "Anil").IsTeaching);

        var bad = (string[])Ravi.Clone(); bad[8] = "Maybe";
        var refused = await Import(false, Asha, bad);
        Assert.False(refused.Success);
        Assert.Contains(refused.Errors, e => e.StartsWith("Row 4:") && e.Contains("Teaching must be Yes or No"));
    }

    [Fact]
    public async Task A_sheet_from_before_the_teaching_column_is_still_accepted_and_leaves_the_flag_alone()
    {
        string[] lab = { "E103", "Mr.", "Anil", "", "More", "anil@viva.edu", "Lab", "Lab Assistant", "No", "", "", "" };
        await Import(false, lab);

        var (bytes, _) = await _service.TemplateAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Staff");
        sheet.Cell(2, 9).Value = "Notes";               // an old sheet has no Teaching column (anything else in that place is not read)
        sheet.Cell(3, 9).Value = "Yes";
        var output = new MemoryStream();
        workbook.SaveAs(output);
        output.Position = 0;

        var result = await _service.ImportAsync(new StaffImportRequest { File = new FormFile(output, 0, output.Length, "File", "old.xlsx"), Preview = false });

        Assert.True(result.Success);
        Assert.Equal(StaffImportActions.Unchanged, result.Rows.Single().Action);
        Assert.False((await _service.ListAsync(new StaffFilter())).Single().IsTeaching);
    }

    [Fact]
    public async Task The_form_saves_the_teaching_flag_and_the_list_filters_on_it()
    {
        var teaching = await _service.SaveAsync(null, Form("E1", "t@mail.com", first: "Tara"));
        var lab = Form("E2", "l@mail.com", first: "Lalit");
        lab.IsTeaching = false;
        var nonTeaching = await _service.SaveAsync(null, lab);

        Assert.True(teaching.IsTeaching);          // the form's default
        Assert.False(nonTeaching.IsTeaching);
        Assert.Equal(new[] { "Tara" }, (await _service.ListAsync(new StaffFilter { Teaching = "teaching" })).Select(s => s.FirstName));
        Assert.Equal(new[] { "Lalit" }, (await _service.ListAsync(new StaffFilter { Teaching = "Non-Teaching" })).Select(s => s.FirstName));
        Assert.Equal(2, (await _service.ListAsync(new StaffFilter { Teaching = "" })).Count);
        Assert.Equal(2, (await _service.ListAsync(new StaffFilter())).Count);

        var edited = Form("E2", "l@mail.com", first: "Lalit");   // saved again with the default
        Assert.True((await _service.SaveAsync(nonTeaching.StaffId, edited)).IsTeaching);
    }

    [Fact]
    public async Task Filters_find_staff_and_people_who_left_are_hidden_unless_asked()
    {
        await Import(false, Asha, Ravi, Meena);
        var ravi = (await _service.ListAsync(new StaffFilter { Search = "kumar" })).Single();
        var leaving = await _service.GetAsync(ravi.StaffId);
        await _service.SaveAsync(ravi.StaffId, new StaffSave
        {
            EmployeeCode = leaving.EmployeeCode, Title = leaving.Title, FirstName = leaving.FirstName, LastName = leaving.LastName,
            Email = leaving.Email, DepartmentId = leaving.DepartmentId, DesignationId = leaving.DesignationId, IsActive = false
        });

        Assert.Equal(new[] { "Meena" }, (await _service.ListAsync(new StaffFilter { Type = "external" })).Select(s => s.FirstName));
        Assert.Equal(new[] { "Asha" }, (await _service.ListAsync(new StaffFilter { Type = "internal" })).Select(s => s.FirstName));
        Assert.Equal(2, (await _service.ListAsync(new StaffFilter { Type = "internal", IncludeInactive = true })).Count);
        Assert.Single(await _service.ListAsync(new StaffFilter { Search = "E101" }));
        var computer = (await _service.OptionsAsync()).Single(o => o.Name == "Computer");
        Assert.Single(await _service.ListAsync(new StaffFilter { DepartmentId = computer.Id }));

        await _service.DeleteAsync(ravi.StaffId);
        Assert.Equal(2, (await _service.ListAsync(new StaffFilter { IncludeInactive = true })).Count);
    }
}
