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

/// <summary>
/// Staff letters as PDF: each type renders, a supervision letter is one page for the person's whole duty chart, cancelled and
/// other-college letters are not found, many letters make one PDF (a chart's letters, or an exam's examiner letters).
/// </summary>
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
    private readonly DutyChart _chart;
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
        _context.AcademicYears.Add(new AcademicYear { AYID = _ay, FullDuration = "2026-2027", ShortDuration = "2026-2027", CollegeId = _college });
        _chart = new DutyChart
        {
            Id = Guid.NewGuid(), CollegeId = _college, AYID = _ay, Name = "Nov-Dec 2026 End-Semester", Half = "Second Half",
            FromDate = new DateTime(2026, 11, 2), ToDate = new DateTime(2026, 11, 13)
        };
        _context.DutyCharts.Add(_chart);
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

    /// <summary>A supervision letter belongs to the duty chart (no exam), an examiner / moderator letter to the exam; duty lines alternate morning and afternoon.</summary>
    private StaffLetter Letter(StaffMaster person, string type, int serial, Guid? college = null, bool cancelled = false, int duties = 0, string? role = null)
    {
        var supervision = type == StaffLetterTypes.Supervision;
        var letter = new StaffLetter
        {
            Id = Guid.NewGuid(), CollegeId = college ?? _college, StaffId = person.StaffId, LetterType = type,
            ExamId = supervision ? null : _exam.ExamId, DutyChartId = supervision ? _chart.Id : null, AYID = _ay,
            SubjectId = supervision ? null : _subject.SubjectId, SerialNo = serial,
            LetterNo = $"VIT/EXAM/2026-27/{StaffLetterTypes.Code(type)}-{serial:D3}", LetterDate = new DateTime(2026, 11, 1),
            Role = role ?? (supervision ? SupervisionRoles.Junior : type == StaffLetterTypes.Examiner ? "Examiner" : "Moderator"),
            StaffName = $"{person.Title} {person.FirstName} {person.LastName}", ExamTitle = supervision ? _chart.Name : "December 2026 Regular", Half = "Second Half"
        };
        for (var i = 0; i < duties; i++)
            letter.Duties.Add(new StaffLetterDuty
            {
                Id = Guid.NewGuid(), CollegeId = letter.CollegeId, LetterId = letter.Id, SortOrder = i, Kind = DutyKinds.Duty, Role = letter.Role,
                Date = $"{2 + i / 2:D2}-11-2026", Time = i % 2 == 0 ? "10:30 AM - 1:30 PM" : "2:30 PM - 5:30 PM"
            });
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
        Assert.Equal("VIT-EXAM-2026-27-DUTY-001 Prof. Asha Patil.pdf", fileName);
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
        Assert.StartsWith("VIT-EXAM-2026-27-" + StaffLetterTypes.Code(type), fileName);
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
    public async Task A_chart_makes_one_pdf_with_a_page_per_letter_and_cancelled_ones_left_out()
    {
        Letter(_asha, StaffLetterTypes.Supervision, 2, duties: 2);
        Letter(_external, StaffLetterTypes.Supervision, 1, duties: 1);
        Letter(_asha, StaffLetterTypes.Supervision, 3, cancelled: true, duties: 1);
        Letter(_asha, StaffLetterTypes.Examiner, 1);

        var (bytes, fileName) = await _service.ChartAsync(_chart.Id);

        AssertPdf(bytes);
        Assert.Equal("Nov-Dec 2026 End-Semester duty letters.pdf", fileName);
        Assert.Equal(2, PageCount(bytes));
    }

    [Fact]
    public async Task A_chart_pdf_refuses_an_unknown_chart_a_chart_of_another_college_and_a_chart_without_letters()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ChartAsync(Guid.NewGuid()));
        var elsewhere = new DutyChart { Id = Guid.NewGuid(), CollegeId = _otherCollege, AYID = _ay, Name = "Elsewhere", FromDate = DateTime.Today, ToDate = DateTime.Today };
        _context.DutyCharts.Add(elsewhere);
        _context.SaveChanges();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ChartAsync(elsewhere.Id));
        var empty = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ChartAsync(_chart.Id));
        Assert.Equal("There are no supervision letters for this duty chart.", empty.Message);
    }

    [Fact]
    public async Task An_exams_appointment_letters_make_one_pdf_with_a_page_each()
    {
        Letter(_asha, StaffLetterTypes.Examiner, 1);
        Letter(_external, StaffLetterTypes.Moderator, 1);
        Letter(_asha, StaffLetterTypes.Moderator, 2, cancelled: true);
        Letter(_asha, StaffLetterTypes.Supervision, 1, duties: 2);   // belongs to the chart, not the exam

        var appointment = await _service.ManyAsync(_exam.ExamId, "appointment");

        AssertPdf(appointment.Bytes);
        Assert.Equal("December 2026 Regular Appointment letters.pdf", appointment.FileName);
        Assert.Equal(2, PageCount(appointment.Bytes));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ManyAsync(_exam.ExamId, "supervision"));
    }

    [Fact]
    public async Task Many_refuses_an_unknown_exam_an_unknown_kind_and_an_exam_without_letters()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ManyAsync(Guid.NewGuid(), "appointment"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ManyAsync(_exam.ExamId, "invitation"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ManyAsync(_exam.ExamId, "appointment"));
    }

    [Theory]
    [InlineData(SupervisionRoles.ChiefConductor, "CHIEF CONDUCTOR")]
    [InlineData(SupervisionRoles.Senior, "SENIOR EXAMINATION SUPERVISOR")]
    [InlineData(SupervisionRoles.Junior, "JUNIOR EXAMINATION SUPERVISOR")]
    [InlineData(SupervisionRoles.NonTeaching, "EXAMINATION DUTY — NON-TEACHING STAFF")]
    [InlineData(null, "EXAMINATION DUTY")]
    public void The_order_is_titled_after_the_highest_role(string? role, string title)
    {
        Assert.Equal(title, StaffLetterPdfService.OrderTitle(role));
    }

    [Fact]
    public void Dates_print_as_day_month_year_and_weekday_and_unreadable_ones_as_written()
    {
        Assert.Equal("10 NOV 2025 (Monday)", StaffLetterPdfService.AssignedDate("10-11-2025"));
        Assert.Equal("10 NOV 2025 (Monday)", StaffLetterPdfService.AssignedDate("2025-11-10"));
        Assert.Equal("someday", StaffLetterPdfService.AssignedDate("someday"));
    }

    [Theory]
    [InlineData("2026-2027", "2026-27")]
    [InlineData("2026 - 2027", "2026-27")]
    [InlineData("2026-27", "2026-27")]
    [InlineData("Year 5", "Year 5")]
    [InlineData(null, "")]
    public void The_academic_year_is_shortened(string? year, string expected)
    {
        Assert.Equal(expected, StaffLetterPdfService.ShortYear(year));
    }

    [Fact]
    public void People_report_one_hour_before_the_first_session_or_per_session_when_the_sessions_differ()
    {
        StaffLetterDuty Line(string time, string kind = DutyKinds.Duty) => new() { Id = Guid.NewGuid(), Date = "02-11-2026", Time = time, Kind = kind };

        Assert.Equal("09:30 AM", StaffLetterPdfService.ReportingTimes(new[] { Line("10:30 AM - 1:30 PM"), Line("10:30 AM - 1:30 PM") }));
        Assert.Equal("09:30 AM for the morning session; 01:30 PM for the afternoon session",
            StaffLetterPdfService.ReportingTimes(new[] { Line("2:30 PM - 5:30 PM"), Line("10:30 AM - 1:30 PM") }));
        Assert.Equal("01:30 PM", StaffLetterPdfService.ReportingTimes(new[] { Line("2:30 PM - 5:30 PM"), Line("Full day", DutyKinds.Valuation) }));
        Assert.Equal("", StaffLetterPdfService.ReportingTimes(new[] { Line("Full day", DutyKinds.Valuation) }));
    }

    [Fact]
    public async Task The_letter_names_the_semesters_timetabled_on_the_charts_days_and_its_academic_year()
    {
        var days = new[] { new DateTime(2026, 11, 2), new DateTime(2026, 11, 3) };
        foreach (var day in days)
            _context.DutyChartDays.Add(new DutyChartDay { Id = Guid.NewGuid(), CollegeId = _college, ChartId = _chart.Id, Date = day, Morning = "10:30 AM - 1:30 PM", FromTimetable = true });
        SubjectMaster Subject(string sem) => new() { SubjectId = Guid.NewGuid(), SubjectCode = "S" + sem, Name = "Subject " + sem, SemId = sem, CourseId = _subject.CourseId, CollegeId = _college };
        void Paper(SubjectMaster subject, DateTime date, ExamMaster? exam = null)
        {
            _context.SubjectMasters.Add(subject);
            _context.TimeTables.Add(new TimeTableMaster
            {
                Id = Guid.NewGuid(), CollegeId = _college, ExamId = (exam ?? _exam).ExamId, SubjectId = subject.SubjectId,
                Date = date.ToString("dd-MM-yyyy"), Time = "10:30 AM - 1:30 PM"
            });
        }
        Paper(Subject("Sem-1"), days[0]);
        Paper(Subject("3"), days[0]);
        Paper(Subject("5"), days[1]);
        Paper(Subject("7"), days[1]);
        Paper(Subject("Sem-5"), days[1]);                                   // a semester counts once
        Paper(Subject("8"), new DateTime(2026, 11, 20));                    // not a day of the chart
        var revaluation = new ExamMaster { ExamId = Guid.NewGuid(), Name = "Reval", ExamType = "Revaluation", AcademicYearAYID = _ay, RevaluationForExamId = _exam.ExamId, IsActive = true, CollegeId = _college };
        _context.Exams.Add(revaluation);
        Paper(Subject("2"), days[0], revaluation);                          // revaluation exams are left out
        var otherYear = new ExamMaster { ExamId = Guid.NewGuid(), Name = "Old", ExamType = "Regular", AcademicYearAYID = Guid.NewGuid(), IsActive = true, CollegeId = _college };
        _context.Exams.Add(otherYear);
        Paper(Subject("4"), days[0], otherYear);                            // another academic year
        _context.SaveChanges();

        var letters = new List<StaffLetter> { Letter(_asha, StaffLetterTypes.Supervision, 1, duties: 1) };
        var years = await _service.AcademicYearsAsync(letters);
        var info = await _service.ChartInfoAsync(_chart.Id, years);

        Assert.Equal(("2026-27", "Semesters I, III, V & VII"), (info.AcademicYear, info.Semesters));
    }

    [Fact]
    public async Task A_chart_without_timetabled_papers_names_no_semesters()
    {
        var letters = new List<StaffLetter> { Letter(_asha, StaffLetterTypes.Supervision, 1, duties: 1) };

        var info = await _service.ChartInfoAsync(_chart.Id, await _service.AcademicYearsAsync(letters));

        Assert.Equal(("2026-27", ""), (info.AcademicYear, info.Semesters));
    }

    [Fact]
    public async Task The_colleges_own_reporting_place_and_guidelines_are_used_else_the_defaults()
    {
        var letter = Letter(_asha, StaffLetterTypes.Supervision, 1, duties: 2);
        var settings = _context.CollegeSettings.Single(s => s.CollegeId == _college);
        settings.ReportingPlace = "Room 101, Main Building";
        settings.DutyGuidelines = "Be on time.\r\n\r\n  Carry your ID card.  \n";
        _context.SaveChanges();

        AssertPdf((await _service.OneAsync(letter.Id)).Bytes);
        var own = await _service.BrandingAsync(_college);
        Assert.Equal("Room 101, Main Building", own.ReportingPlace);
        Assert.Equal(new[] { "Be on time.", "Carry your ID card." }, own.Guidelines);

        settings.ReportingPlace = "  ";
        settings.DutyGuidelines = "";
        _context.SaveChanges();
        var defaults = await _service.BrandingAsync(_college);
        Assert.Equal("the Examination Control Room", defaults.ReportingPlace);
        Assert.Equal(3, defaults.Guidelines.Count);
        Assert.StartsWith("Follow the University's examination conduct rules", defaults.Guidelines[0]);

        settings.DutyGuidelines = string.Join('\n', Enumerable.Range(1, 14).Select(i => "Rule " + i));
        _context.SaveChanges();
        Assert.Equal(10, (await _service.BrandingAsync(_college)).Guidelines.Count);   // at most ten
    }
}
