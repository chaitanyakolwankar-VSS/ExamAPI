using System.Text;
using System.Text.RegularExpressions;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Files;
using ExamAPI.Services.Staff;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ExamAPI.Tests;

/// <summary>Staff letters as PDF: each type renders, cancelled and other-college letters are not found, many letters make one PDF.</summary>
public sealed class StaffLetterPdfTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "staff-pdf-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly FileStorage _storage;
    private readonly StaffLetterPdfService _service;
    private readonly ExamMaster _exam;
    private readonly SubjectMaster _subject;
    private readonly StaffMaster _asha;
    private readonly StaffMaster _external;

    public StaffLetterPdfTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        Directory.CreateDirectory(_base);
        _storage = new FileStorage(_base, null, Path.Combine(_base, "store"));

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new StaffLetterPdfService(_context, _storage);

        _context.Colleges.Add(new College
        {
            CollegeId = _college, Name = "VIVA Institute of Technology", CollegeCode = "VIT", CollegeCenter = "Virar",
            ContactEmail = "office@viva.edu", ContactPhone = "0250", Address = "Virar (East), Palghar 401305"
        });
        _context.Colleges.Add(new College { CollegeId = _otherCollege, Name = "Other College", CollegeCode = "OTH", CollegeCenter = "Pune", ContactEmail = "o@other.edu", ContactPhone = "020" });
        var course = new CourseMaster { CourseId = Guid.NewGuid(), Name = "Computer Engineering", CourseCode = "CMP", CollegeId = _college };
        _context.CourseMasters.Add(course);
        _exam = new ExamMaster
        {
            ExamId = Guid.NewGuid(), Name = "December 2026", ExamType = "Regular", AcademicYearAYID = _ay, CourseId = course.CourseId,
            IsActive = true, CollegeId = _college
        };
        _context.Exams.Add(_exam);
        _subject = new SubjectMaster
        {
            SubjectId = Guid.NewGuid(), SubjectCode = "CSC601", Name = "System Programming", SemId = "6", Pattern = "Rev-2019",
            CourseId = course.CourseId, CollegeId = _college
        };
        _context.SubjectMasters.Add(_subject);
        var designation = new StaffOption { Id = Guid.NewGuid(), CollegeId = _college, Kind = StaffOptionKinds.Designation, Name = "Assistant Professor" };
        _context.StaffOptions.Add(designation);
        _context.CollegeSettings.Add(new CollegeSettings { Id = Guid.NewGuid(), CollegeId = _college, CapContact = "9876543210" });

        _asha = Person("Prof.", "Asha", "Patil", false, designation.Id);
        _external = Person("Dr.", "Ravi", "Kumar", true, null);
        _external.ExternalCollege = "Pune Institute";
        _external.ExternalCity = "Pune";
        _context.SaveChanges();
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    private StaffMaster Person(string title, string first, string last, bool external, Guid? designationId)
    {
        var s = new StaffMaster
        {
            StaffId = Guid.NewGuid(), CollegeId = _college, EmployeeCode = external ? null : "E" + first, Title = title, FirstName = first,
            LastName = last, Email = first.ToLowerInvariant() + "@viva.edu", IsExternal = external, DesignationId = designationId
        };
        _context.StaffMasters.Add(s);
        return s;
    }

    private StaffLetter Letter(StaffMaster person, string type, int serial, Guid? college = null, bool cancelled = false, int duties = 0)
    {
        var letter = new StaffLetter
        {
            Id = Guid.NewGuid(), CollegeId = college ?? _college, StaffId = person.StaffId, LetterType = type, ExamId = _exam.ExamId, AYID = _ay,
            SubjectId = type == StaffLetterTypes.Supervision ? null : _subject.SubjectId, SerialNo = serial,
            LetterNo = $"VIT/EXAM/{StaffLetterTypes.Code(type)}/2026-27/{serial:D4}", LetterDate = new DateTime(2026, 11, 20),
            Role = type == StaffLetterTypes.Supervision ? SupervisionRoles.Junior : type == StaffLetterTypes.Examiner ? "Examiner" : "Moderator",
            StaffName = $"{person.Title} {person.FirstName} {person.LastName}", ExamTitle = "December 2026 Regular", Half = "Second Half"
        };
        for (var i = 0; i < duties; i++)
            letter.Duties.Add(new StaffLetterDuty { Id = Guid.NewGuid(), CollegeId = letter.CollegeId, LetterId = letter.Id, Date = $"{i + 1:D2}-12-2026", Time = "10:00 AM - 01:00 PM", SortOrder = i });
        _context.StaffLetters.Add(letter);
        _context.SaveChanges();
        if (cancelled)
        {
            letter.IsDeleted = true;   // a new row is always saved as live, so cancel it as the letters screen does
            letter.DeletedAt = DateTime.UtcNow;
            _context.SaveChanges();
        }
        return letter;
    }

    private static void AssertPdf(byte[] bytes)
    {
        Assert.NotEmpty(bytes);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    private static int PageCount(byte[] bytes) =>
        Regex.Matches(Encoding.Latin1.GetString(bytes), @"/Type\s*/Page(?![a-zA-Z])").Count;

    [Fact]
    public async Task A_supervision_letter_is_a_one_page_pdf_named_after_its_number_and_the_person()
    {
        var letter = Letter(_asha, StaffLetterTypes.Supervision, 1, duties: 3);

        var (bytes, fileName) = await _service.OneAsync(letter.Id);

        AssertPdf(bytes);
        Assert.Equal("VIT-EXAM-SUP-2026-27-0001 Prof. Asha Patil.pdf", fileName);
        Assert.Equal(1, PageCount(bytes));
    }

    [Theory]
    [InlineData(StaffLetterTypes.Examiner)]
    [InlineData(StaffLetterTypes.Moderator)]
    public async Task An_appointment_letter_renders_for_own_and_external_staff(string type)
    {
        var own = Letter(_asha, type, 1);
        var external = Letter(_external, type, 2);

        AssertPdf((await _service.OneAsync(own.Id)).Bytes);
        var (bytes, fileName) = await _service.OneAsync(external.Id);
        AssertPdf(bytes);
        Assert.StartsWith("VIT-EXAM-" + StaffLetterTypes.Code(type), fileName);
        Assert.EndsWith("Dr. Ravi Kumar.pdf", fileName);
    }

    [Fact]
    public async Task Letters_render_with_the_college_images_and_without_any()
    {
        var banner = await _storage.SaveAsync(UploadsTests.Png, FileStorage.CollegeBannersFolder, "banner.png");
        var sign = await _storage.SaveAsync(UploadsTests.Png, FileStorage.CollegeSignaturesFolder, "sign.png");
        var seal = await _storage.SaveAsync(UploadsTests.Png, FileStorage.CollegeSignaturesFolder, "seal.png");
        var college = _context.Colleges.Single(c => c.CollegeId == _college);
        college.LogoBannerUrl = banner;
        college.ControllerSignUrl = sign;
        college.PrincipalSignUrl = sign;
        college.SealUrl = seal;
        _context.SaveChanges();

        var supervision = Letter(_asha, StaffLetterTypes.Supervision, 1, duties: 2);
        var appointment = Letter(_asha, StaffLetterTypes.Examiner, 1);

        AssertPdf((await _service.OneAsync(supervision.Id)).Bytes);
        AssertPdf((await _service.OneAsync(appointment.Id)).Bytes);
    }

    [Fact]
    public async Task A_long_duty_list_still_makes_a_pdf()
    {
        var letter = Letter(_asha, StaffLetterTypes.Supervision, 1, duties: 60);

        AssertPdf((await _service.OneAsync(letter.Id)).Bytes);
    }

    [Fact]
    public async Task A_cancelled_letter_is_not_found()
    {
        var letter = Letter(_asha, StaffLetterTypes.Supervision, 1, cancelled: true, duties: 1);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.OneAsync(letter.Id));
    }

    [Fact]
    public async Task A_letter_of_another_college_is_not_found()
    {
        var elsewhere = new StaffMaster
        {
            StaffId = Guid.NewGuid(), CollegeId = _otherCollege, EmployeeCode = "X1", Title = "Mr.", FirstName = "Sam", LastName = "Other", Email = "sam@other.edu"
        };
        _context.StaffMasters.Add(elsewhere);
        var letter = Letter(elsewhere, StaffLetterTypes.Supervision, 1, college: _otherCollege, duties: 1);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.OneAsync(letter.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.OneAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Many_letters_make_one_pdf_with_a_page_each_and_cancelled_ones_left_out()
    {
        Letter(_asha, StaffLetterTypes.Supervision, 1, duties: 2);
        Letter(_external, StaffLetterTypes.Supervision, 2, duties: 1);
        Letter(_asha, StaffLetterTypes.Supervision, 3, cancelled: true, duties: 1);
        Letter(_asha, StaffLetterTypes.Examiner, 1);
        Letter(_external, StaffLetterTypes.Moderator, 1);

        var supervision = await _service.ManyAsync(_exam.ExamId, "supervision");
        var appointment = await _service.ManyAsync(_exam.ExamId, "appointment");

        AssertPdf(supervision.Bytes);
        AssertPdf(appointment.Bytes);
        Assert.Equal("December 2026 Regular Supervision letters.pdf", supervision.FileName);
        Assert.Equal("December 2026 Regular Appointment letters.pdf", appointment.FileName);
        Assert.Equal(2, PageCount(supervision.Bytes));
        Assert.Equal(2, PageCount(appointment.Bytes));
    }

    [Fact]
    public async Task Many_refuses_an_unknown_exam_an_unknown_kind_and_an_exam_without_letters()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ManyAsync(Guid.NewGuid(), "supervision"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ManyAsync(_exam.ExamId, "invitation"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ManyAsync(_exam.ExamId, "supervision"));
    }
}
