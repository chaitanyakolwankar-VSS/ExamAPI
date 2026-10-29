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

/// <summary>
/// Duty charts: days come from the timetables, the whole chart is checked and saved at once, each person with a duty
/// has one numbered letter that follows the chart, and the Excel sheet round-trips.
/// </summary>
public sealed class DutyChartTests
{
    private const string Morning = "10:30 AM - 1:30 PM";
    private const string Afternoon = "2:30 PM - 5:30 PM";

    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly Guid _ay = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly DutyChartService _service;
    private readonly CourseMaster _computer;
    private readonly CourseMaster _it;
    private readonly ExamMaster _computerExam;
    private readonly ExamMaster _itExam;
    private readonly StaffMaster _asha;   // teaching
    private readonly StaffMaster _ravi;   // teaching
    private readonly StaffMaster _meena;  // teaching
    private readonly StaffMaster _om;     // non-teaching

    public DutyChartTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new DutyChartService(_context);

        _context.Colleges.Add(new College { CollegeId = _college, Name = "VIVA Institute of Technology", CollegeCode = "VIT", CollegeCenter = "Virar", ContactEmail = "office@viva.edu", ContactPhone = "0250" });
        _context.Colleges.Add(new College { CollegeId = _otherCollege, Name = "Other College", CollegeCode = "OTH", CollegeCenter = "Pune", ContactEmail = "o@other.edu", ContactPhone = "020" });
        _context.AcademicYears.Add(new AcademicYear { AYID = _ay, FullDuration = "2026-2027", CollegeId = _college });
        _computer = new CourseMaster { CourseId = Guid.NewGuid(), Name = "Computer", CourseCode = "CMP", CollegeId = _college };
        _it = new CourseMaster { CourseId = Guid.NewGuid(), Name = "Information Technology", CourseCode = "IT", CollegeId = _college };
        _context.CourseMasters.AddRange(_computer, _it);
        _context.SaveChanges();

        _computerExam = Exam("May 2026", _computer);
        _itExam = Exam("May 2026", _it);
        _context.SaveChanges();

        // Mon 11 May: morning (2 papers) and afternoon. Tue 12: three morning papers, two share the usual time. Wed 13: afternoon only.
        Paper(_computerExam, _computer, "S1", "11-05-2026", Morning);
        Paper(_itExam, _it, "S2", "11-05-2026", Morning);
        Paper(_computerExam, _computer, "S3", "11-05-2026", Afternoon);
        Paper(_computerExam, _computer, "S4", "12-05-2026", Morning);
        Paper(_itExam, _it, "S5", "12-05-2026", Morning);
        Paper(_itExam, _it, "S6", "12-05-2026", "10:00 AM - 1:00 PM");
        Paper(_itExam, _it, "S7", "13-05-2026", Afternoon);
        Paper(_itExam, _it, "S8", "20-05-2026", Morning);   // after the chart's last date
        _context.SaveChanges();

        _asha = Person("E101", "Prof.", "Asha", "R.", "Patil", "asha@viva.edu");
        _ravi = Person("E102", "Dr.", "Ravi", null, "Kumar", "ravi@viva.edu");
        _meena = Person("E103", "Dr.", "Meena", null, "Shah", "meena@viva.edu");
        _om = Person("E104", "Mr.", "Om", null, "Das", "om@viva.edu", teaching: false);
        _context.SaveChanges();
    }

    // ------------------------------------------------------------------ fixtures

    private ExamMaster Exam(string name, CourseMaster course, Guid? revalFor = null, Guid? college = null)
    {
        var e = new ExamMaster
        {
            ExamId = Guid.NewGuid(), Name = name, ExamType = "Regular", AcademicYearAYID = _ay, CourseId = course.CourseId,
            RevaluationForExamId = revalFor, IsActive = true, CollegeId = college ?? _college
        };
        _context.Exams.Add(e);
        return e;
    }

    private void Paper(ExamMaster exam, CourseMaster course, string code, string date, string time)
    {
        var s = new SubjectMaster
        {
            SubjectId = Guid.NewGuid(), SubjectCode = code, Name = code + " name", SemId = "Sem-2", Pattern = "NEP",
            CourseId = course.CourseId, CollegeId = exam.CollegeId
        };
        _context.SubjectMasters.Add(s);
        _context.TimeTables.Add(new TimeTableMaster
        {
            Id = Guid.NewGuid(), ExamId = exam.ExamId, CourseId = course.CourseId, SubjectId = s.SubjectId, Date = date, Time = time, CollegeId = exam.CollegeId
        });
    }

    private StaffMaster Person(string code, string title, string first, string? middle, string last, string email,
        bool teaching = true, bool external = false, bool active = true, Guid? college = null)
    {
        var s = new StaffMaster
        {
            StaffId = Guid.NewGuid(), CollegeId = college ?? _college, EmployeeCode = code, Title = title, FirstName = first,
            MiddleName = middle, LastName = last, Email = email, IsTeaching = teaching, IsExternal = external, IsActive = active
        };
        _context.StaffMasters.Add(s);
        return s;
    }

    private static DutyChartSave Fields(string from = "2026-05-11", string to = "2026-05-15", string name = "May 2026 End-Semester", string half = "First Half") =>
        new() { Name = name, Half = half, FromDate = from, ToDate = to, Note = "Be on campus for 8 hours" };

    private Task<DutyChartDetailDto> NewChart(string from = "2026-05-11", string to = "2026-05-15")
    {
        var fields = Fields(from, to);
        fields.Ayid = _ay;
        return _service.CreateAsync(fields);
    }

    private static DutyChartEntryDto Duty(StaffMaster who, string date, string session, string? role = null) =>
        new() { StaffId = who.StaffId, Date = date, Kind = "duty", Session = session, Role = role };

    private static DutyChartEntryDto Valuation(StaffMaster who, string date) => new() { StaffId = who.StaffId, Date = date, Kind = "valuation" };

    private static DutyChartEntryDto Other(StaffMaster who, string date, string? note) => new() { StaffId = who.StaffId, Date = date, Kind = "other", Note = note };

    private Task<DutyChartSaveResult> Save(Guid chartId, string? letterDate, params DutyChartEntryDto[] entries) =>
        _service.SaveEntriesAsync(chartId, new DutyChartEntriesSave { LetterDate = letterDate, Entries = entries.ToList() });

    private async Task<string> Refused(Guid chartId, string? letterDate, params DutyChartEntryDto[] entries) =>
        (await Assert.ThrowsAsync<InvalidOperationException>(() => Save(chartId, letterDate, entries))).Message;

    private static string Sig(IEnumerable<DutyChartEntryDto> entries) =>
        string.Join("|", entries.OrderBy(e => e.StaffId).ThenBy(e => e.Date).Select(e => $"{e.StaffId}/{e.Date}/{e.Kind}/{e.Session}/{e.Role}/{e.Note}"));

    // ------------------------------------------------------------------ days from the timetables

    [Fact]
    public async Task A_new_chart_gets_its_days_from_the_timetables_split_into_morning_and_afternoon()
    {
        // Not part of the chart: a revaluation exam, another college's exam.
        var reval = Exam("May 2026", _computer, revalFor: _computerExam.ExamId);
        var theirCourse = new CourseMaster { CourseId = Guid.NewGuid(), Name = "Mechanical", CourseCode = "MEC", CollegeId = _otherCollege };
        _context.CourseMasters.Add(theirCourse);
        var theirs = Exam("May 2026", theirCourse, college: _otherCollege);
        _context.SaveChanges();
        Paper(reval, _computer, "R1", "14-05-2026", Morning);
        Paper(theirs, theirCourse, "T1", "15-05-2026", Morning);
        _context.SaveChanges();

        var chart = await NewChart();

        Assert.Equal("May 2026 End-Semester", chart.Chart.Name);
        Assert.Equal(("2026-05-11", "2026-05-15", "First Half", "Be on campus for 8 hours"), (chart.Chart.FromDate, chart.Chart.ToDate, chart.Chart.Half, chart.Chart.Note));
        Assert.Equal(new[] { "2026-05-11", "2026-05-12", "2026-05-13" }, chart.Days.Select(d => d.Date));
        Assert.Equal(3, chart.Chart.DayCount);

        var monday = chart.Days[0];
        Assert.Equal(("Monday", Morning, Afternoon, true, 3), (monday.Weekday, monday.Morning, monday.Afternoon, monday.FromTimetable, monday.Papers));
        var tuesday = chart.Days[1];
        Assert.Equal(Morning, tuesday.Morning);   // the most common time of the day
        Assert.Null(tuesday.Afternoon);
        Assert.Equal(3, tuesday.Papers);
        var wednesday = chart.Days[2];
        Assert.Null(wednesday.Morning);
        Assert.Equal(Afternoon, wednesday.Afternoon);
        Assert.Equal(1, wednesday.Papers);

        Assert.Equal(SupervisionRoles.All, chart.Roles);
        Assert.Empty(chart.Entries);
        Assert.Empty(chart.Letters);
        Assert.Equal((0, 0, 0, 0), (chart.Chart.StaffCount, chart.Chart.DutyCount, chart.Chart.LetterCount, chart.Entries.Count));
    }

    [Fact]
    public async Task A_chart_needs_a_name_valid_dates_and_an_academic_year()
    {
        var noName = Fields(name: " "); noName.Ayid = _ay;
        Assert.Contains("name", (await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(noName))).Message);
        var backwards = Fields("2026-05-15", "2026-05-11"); backwards.Ayid = _ay;
        Assert.Contains("before the first date", (await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(backwards))).Message);
        var badDate = Fields("11/05/2026"); badDate.Ayid = _ay;
        Assert.Contains("not a valid date", (await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(badDate))).Message);
        var badHalf = Fields(half: "Third"); badHalf.Ayid = _ay;
        Assert.Contains("First Half, Second Half", (await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(badHalf))).Message);
        var noYear = Fields();
        Assert.Contains("academic year", (await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(noYear))).Message);
        Assert.Empty(await _context.DutyCharts.ToListAsync());
    }

    [Fact]
    public async Task The_chart_list_is_newest_first_with_counts()
    {
        var first = await NewChart();
        var later = Fields("2026-06-01", "2026-06-05", "June 2026"); later.Ayid = _ay;
        var second = await _service.CreateAsync(later);
        await Save(first.Chart.Id, "2026-04-30", Duty(_asha, "2026-05-11", "MA"), Duty(_ravi, "2026-05-12", "M"), Valuation(_meena, "2026-05-11"));

        var list = await _service.ListAsync(_ay);

        Assert.Equal(new[] { "June 2026", "May 2026 End-Semester" }, list.Select(c => c.Name));
        var may = list[1];
        Assert.Equal((3, 3, 3, 2), (may.DayCount, may.StaffCount, may.DutyCount, may.LetterCount));
        Assert.Equal(second.Chart.Id, list[0].Id);
        Assert.Equal((0, 0, 0, 0), (list[0].DayCount, list[0].StaffCount, list[0].DutyCount, list[0].LetterCount));
        Assert.Empty(await _service.ListAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Changing_the_dates_adds_missing_timetable_days_and_shrinking_is_refused_when_a_removed_day_has_entries()
    {
        var chart = await NewChart("2026-05-11", "2026-05-12");
        Assert.Equal(2, chart.Days.Count);

        var wider = Fields("2026-05-11", "2026-05-31"); wider.Ayid = _ay;
        var widened = await _service.UpdateAsync(chart.Chart.Id, wider);
        Assert.Equal(new[] { "2026-05-11", "2026-05-12", "2026-05-13", "2026-05-20" }, widened.Days.Select(d => d.Date));

        await Save(chart.Chart.Id, "2026-04-30", Duty(_asha, "2026-05-20", "M"));
        var shorter = Fields("2026-05-11", "2026-05-15"); shorter.Ayid = _ay;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(chart.Chart.Id, shorter));
        Assert.Contains("Prof. Asha R. Patil", ex.Message);
        Assert.Contains("20-05-2026", ex.Message);
        Assert.Equal(4, (await _service.GetAsync(chart.Chart.Id)).Days.Count);

        await Save(chart.Chart.Id, "2026-04-30", Duty(_asha, "2026-05-11", "M"));   // moves the entry off the last day
        var shrunk = await _service.UpdateAsync(chart.Chart.Id, shorter);
        Assert.Equal(new[] { "2026-05-11", "2026-05-12", "2026-05-13" }, shrunk.Days.Select(d => d.Date));
        Assert.Equal("2026-05-15", shrunk.Chart.ToDate);

        var otherYear = Fields(); otherYear.Ayid = Guid.NewGuid();
        Assert.Contains("cannot be changed", (await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(chart.Chart.Id, otherYear))).Message);
    }

    [Fact]
    public async Task Refreshing_adds_new_timetable_days_and_fills_empty_times_but_keeps_hand_edits()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        // Monday's morning is changed by hand; Tuesday gets a time of its own by hand; Wednesday stays as the timetable had it.
        var edited = await _service.SaveDaysAsync(id, new List<DutyChartDaySave>
        {
            new() { Date = "2026-05-11", Morning = "9:00 AM - 12:00 PM", Afternoon = Afternoon },
            new() { Date = "2026-05-12", Morning = Morning },
            new() { Date = "2026-05-13", Afternoon = Afternoon },
            new() { Date = "2026-05-14", Morning = "11:00 AM - 2:00 PM" }
        });
        Assert.Equal(new[] { false, true, true, false }, edited.Days.Select(d => d.FromTimetable));   // only the changed or added days become hand-edited

        // The timetable now has a Wednesday morning and a Thursday.
        Paper(_itExam, _it, "S9", "13-05-2026", Morning);
        Paper(_itExam, _it, "S10", "15-05-2026", Afternoon);
        _context.SaveChanges();

        var refreshed = await _service.RefreshDaysAsync(id);

        var monday = refreshed.Days.Single(d => d.Date == "2026-05-11");
        Assert.Equal("9:00 AM - 12:00 PM", monday.Morning);   // hand edit kept
        Assert.False(monday.FromTimetable);
        var wednesday = refreshed.Days.Single(d => d.Date == "2026-05-13");
        Assert.Equal((Morning, Afternoon, true), (wednesday.Morning, wednesday.Afternoon, wednesday.FromTimetable));   // empty time filled
        var thursday = refreshed.Days.Single(d => d.Date == "2026-05-14");
        Assert.Equal(("11:00 AM - 2:00 PM", null), (thursday.Morning, thursday.Afternoon));   // a hand-edited day is not touched
        Assert.False(thursday.FromTimetable);
        var friday = refreshed.Days.Single(d => d.Date == "2026-05-15");   // a new timetable day is added
        Assert.Equal((null, Afternoon, true), (friday.Morning, friday.Afternoon, friday.FromTimetable));
        Assert.Equal(5, refreshed.Days.Count);
    }

    [Fact]
    public async Task Saving_days_refuses_bad_days_and_removing_a_day_or_session_that_people_use()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        await Save(id, "2026-04-30", Duty(_asha, "2026-05-11", "MA"), Duty(_ravi, "2026-05-12", "M"));

        List<DutyChartDaySave> Keep(params (string Date, string? M, string? A)[] days) => days.Select(d => new DutyChartDaySave { Date = d.Date, Morning = d.M, Afternoon = d.A }).ToList();

        var dayGone = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveDaysAsync(id, Keep(("2026-05-11", Morning, Afternoon), ("2026-05-13", null, Afternoon))));
        Assert.Contains("Dr. Ravi Kumar has an entry on 12-05-2026", dayGone.Message);

        var sessionGone = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveDaysAsync(id, Keep(("2026-05-11", Morning, null), ("2026-05-12", Morning, null), ("2026-05-13", null, Afternoon))));
        Assert.Contains("Prof. Asha R. Patil is on duty in the afternoon of 11-05-2026", sessionGone.Message);

        var bad = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveDaysAsync(id, Keep(
            ("2026-05-11", Morning, Afternoon), ("2026-05-11", Morning, null), ("2026-05-12", Morning, null), ("2026-06-01", Morning, null), ("2026-05-14", null, null), ("tomorrow", Morning, null))));
        Assert.Contains("11-05-2026 is listed more than once", bad.Message);
        Assert.Contains("01-06-2026 is outside the dates", bad.Message);
        Assert.Contains("14-05-2026 needs a morning or an afternoon time", bad.Message);
        Assert.Contains("\"tomorrow\" is not a valid date", bad.Message);

        // Nothing changed.
        Assert.Equal(3, (await _service.GetAsync(id)).Days.Count);

        // A day nobody uses can go, and a new day is added by hand.
        var saved = await _service.SaveDaysAsync(id, Keep(("2026-05-11", Morning, Afternoon), ("2026-05-12", Morning, null), ("2026-05-14", Morning, null)));
        Assert.Equal(new[] { "2026-05-11", "2026-05-12", "2026-05-14" }, saved.Days.Select(d => d.Date));
        Assert.Equal(new[] { true, true, false }, saved.Days.Select(d => d.FromTimetable));
    }

    // ------------------------------------------------------------------ entries: validation

    [Fact]
    public async Task Entries_are_checked_all_or_nothing_with_every_problem_listed()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        var left = Person("E105", "Mr.", "Gone", null, "Away", "gone@viva.edu", active: false);
        var external = Person("", "Dr.", "Neha", null, "Iyer", "neha@other.edu", external: true);
        _context.SaveChanges();
        await Save(id, "2026-04-30", Duty(_meena, "2026-05-11", "M"));   // already saved: must survive a refused save

        var message = await Refused(id, "2026-04-30",
            Duty(_asha, "2026-05-14", "M"),                                   // not a day of the chart
            Duty(_asha, "2026-05-12", "AM"),                                  // not a session code
            Duty(_ravi, "2026-05-13", "M"),                                   // Wednesday has no morning
            Duty(_ravi, "2026-05-12", "A"),                                   // Tuesday has no afternoon
            Duty(_ravi, "2026-05-11", "M", "Head Boy"),                       // not a role
            new DutyChartEntryDto { StaffId = _ravi.StaffId, Date = "2026-05-12", Kind = "party" },
            Other(_om, "2026-05-11", new string('x', 101)),                   // note too long
            Duty(_om, "2026-05-11", "M"),                                     // once per person per day
            Duty(left, "2026-05-11", "M"),
            Duty(external, "2026-05-11", "M"),
            Duty(_meena, "11-05-2026", "M"),                                  // not yyyy-MM-dd
            new DutyChartEntryDto { StaffId = Guid.NewGuid(), Date = "2026-05-11", Kind = "duty", Session = "M" });

        Assert.Contains("Prof. Asha R. Patil has an entry on 14-05-2026, which is not a day of this chart", message);
        Assert.Contains("Prof. Asha R. Patil needs a session (M, A or MA) on 12-05-2026", message);
        Assert.Contains("Dr. Ravi Kumar is given the morning of 13-05-2026, but there is no morning session that day", message);
        Assert.Contains("Dr. Ravi Kumar is given the afternoon of 12-05-2026, but there is no afternoon session that day", message);
        Assert.Contains("Dr. Ravi Kumar has the role \"Head Boy\"", message);
        Assert.Contains("Dr. Ravi Kumar has an entry on 12-05-2026 that is not a duty, valuation or other work", message);
        Assert.Contains("The note for Mr. Om Das on 11-05-2026 is longer than 100 characters", message);
        Assert.Contains("Mr. Om Das appears more than once on 11-05-2026", message);
        Assert.Contains("Mr. Gone Away has left", message);
        Assert.Contains("Dr. Neha Iyer is an external member of staff", message);
        Assert.Contains("Dr. Meena Shah has an entry with a date that is not valid", message);
        Assert.Contains("a member of staff in the list was not found", message);

        var chartNow = await _service.GetAsync(id);
        Assert.Equal(new[] { _meena.StaffId }, chartNow.Entries.Select(e => e.StaffId));   // nothing was saved
        Assert.Single(chartNow.Letters);
    }

    [Fact]
    public async Task A_duty_without_a_role_gets_the_default_and_valuation_and_other_ignore_session_and_role()
    {
        var chart = await NewChart();

        await Save(chart.Chart.Id, "2026-04-30",
            Duty(_asha, "2026-05-11", "ma"),
            Duty(_om, "2026-05-11", "M"),
            Duty(_ravi, "2026-05-12", "M", "senior supervisor"),
            new DutyChartEntryDto { StaffId = _meena.StaffId, Date = "2026-05-11", Kind = "Valuation", Session = "M", Role = "Chief Conductor", Note = "ignored" },
            new DutyChartEntryDto { StaffId = _meena.StaffId, Date = "2026-05-12", Kind = "other", Session = "A", Role = SupervisionRoles.Junior, Note = "  Exam   cell " });

        var entries = (await _service.GetAsync(chart.Chart.Id)).Entries;
        var asha = entries.Single(e => e.StaffId == _asha.StaffId);
        Assert.Equal(("duty", "MA", SupervisionRoles.Junior, null), (asha.Kind, asha.Session, asha.Role, asha.Note));
        Assert.Equal(SupervisionRoles.NonTeaching, entries.Single(e => e.StaffId == _om.StaffId).Role);
        Assert.Equal(SupervisionRoles.Senior, entries.Single(e => e.StaffId == _ravi.StaffId).Role);
        var valuation = entries.Single(e => e.StaffId == _meena.StaffId && e.Kind == "valuation");
        Assert.Equal((null, null, null), (valuation.Session, valuation.Role, valuation.Note));
        var other = entries.Single(e => e.StaffId == _meena.StaffId && e.Kind == "other");
        Assert.Equal((null, null, "Exam cell"), (other.Session, other.Role, other.Note));
    }

    // ------------------------------------------------------------------ letters

    [Fact]
    public async Task Each_person_with_a_duty_gets_one_numbered_letter_with_a_line_per_session()
    {
        var chart = await NewChart();

        var result = await Save(chart.Chart.Id, "2026-04-30",
            Duty(_asha, "2026-05-13", "A"),
            Duty(_asha, "2026-05-11", "MA", SupervisionRoles.Senior),
            Valuation(_asha, "2026-05-12"),
            Duty(_ravi, "2026-05-12", "M"),
            Other(_ravi, "2026-05-13", "Exam cell"),
            Other(_om, "2026-05-11", null),
            Duty(_om, "2026-05-12", "M"),
            Valuation(_meena, "2026-05-11"));   // valuation alone is no duty: no letter

        Assert.Equal((3, 0, 0, 0), (result.Created, result.Updated, result.Unchanged, result.Removed));
        Assert.Equal("Saved: 3 new, 0 updated, 0 unchanged, 0 cancelled.", result.Message);

        var detail = await _service.GetAsync(chart.Chart.Id);
        Assert.Equal(new[] { "VIT/EXAM/2026-27/DUTY-001", "VIT/EXAM/2026-27/DUTY-002", "VIT/EXAM/2026-27/DUTY-003" }, detail.Letters.Select(l => l.LetterNo));
        Assert.Equal(new[] { "Prof. Asha R. Patil", "Mr. Om Das", "Dr. Ravi Kumar" }, detail.Letters.Select(l => l.StaffName));   // by first name
        Assert.DoesNotContain(detail.Letters, l => l.StaffId == _meena.StaffId);
        Assert.Equal((3, 4, 5), (detail.Chart.LetterCount, detail.Chart.StaffCount, detail.Chart.DutyCount));
        Assert.All(detail.Letters, l => Assert.Equal("2026-04-30", l.LetterDate));

        var asha = detail.Letters[0];
        Assert.Equal(SupervisionRoles.Senior, asha.Role);   // the highest role of her duties
        Assert.Equal(new[]
        {
            ("2026-05-11", Morning, "duty", SupervisionRoles.Senior),
            ("2026-05-11", Afternoon, "duty", SupervisionRoles.Senior),
            ("2026-05-12", "Full day", "valuation", "Valuation"),
            ("2026-05-13", Afternoon, "duty", SupervisionRoles.Junior)
        }, asha.Lines.Select(l => (l.Date, l.Time, l.Kind, l.Role)));

        var om = detail.Letters[1];
        Assert.Equal(SupervisionRoles.NonTeaching, om.Role);
        Assert.Equal(new[] { ("2026-05-11", "Full day", "other", "Other work"), ("2026-05-12", Morning, "duty", SupervisionRoles.NonTeaching) },
            om.Lines.Select(l => (l.Date, l.Time, l.Kind, l.Role)));
        Assert.Equal(("Full day", "Exam cell"), (detail.Letters[2].Lines[1].Time, detail.Letters[2].Lines[1].Role));

        var stored = await _context.StaffLetters.ToListAsync();
        Assert.All(stored, l => Assert.Equal(StaffLetterTypes.Supervision, l.LetterType));
        Assert.All(stored, l => Assert.Equal(chart.Chart.Id, l.DutyChartId));
        Assert.All(stored, l => Assert.Null(l.ExamId));
        Assert.All(stored, l => Assert.Equal(_ay, l.AYID));
        Assert.All(stored, l => Assert.Equal("May 2026 End-Semester", l.ExamTitle));
        Assert.All(stored, l => Assert.Equal("First Half", l.Half));
        Assert.All(stored, l => Assert.Equal(_college, l.CollegeId));
        // Stored line dates are dd-MM-yyyy text, as the timetables spell them.
        var ashaLetterId = stored.Single(l => l.StaffId == _asha.StaffId).Id;
        Assert.Equal("11-05-2026", (await _context.StaffLetterDuties.OrderBy(d => d.SortOrder).FirstAsync(d => d.LetterId == ashaLetterId)).Date);
    }

    [Fact]
    public async Task Saving_again_keeps_numbers_and_dates_and_only_changed_letters_are_updated()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        await Save(id, "2026-04-30", Duty(_asha, "2026-05-11", "M"), Duty(_ravi, "2026-05-12", "M"));

        var result = await Save(id, "2026-05-05",
            Duty(_asha, "2026-05-11", "M", SupervisionRoles.ChiefConductor),   // role changed
            Duty(_ravi, "2026-05-12", "M"));                                    // same

        Assert.Equal((0, 1, 1, 0), (result.Created, result.Updated, result.Unchanged, result.Removed));
        var letters = (await _service.GetAsync(id)).Letters;
        var asha = letters.Single(l => l.StaffId == _asha.StaffId);
        Assert.Equal(("VIT/EXAM/2026-27/DUTY-001", "2026-04-30", SupervisionRoles.ChiefConductor), (asha.LetterNo, asha.LetterDate, asha.Role));   // the new date is for new letters only
        Assert.Single(asha.Lines);
        Assert.Equal(2, await _context.StaffLetterDuties.CountAsync());   // lines are replaced, not piled up

        // The same chart again, no date needed: everything is unchanged.
        var same = await Save(id, null, Duty(_asha, "2026-05-11", "M", SupervisionRoles.ChiefConductor), Duty(_ravi, "2026-05-12", "M"));
        Assert.Equal((0, 0, 2, 0), (same.Created, same.Updated, same.Unchanged, same.Removed));

        // A new line for Ravi: updated, same number.
        var more = await Save(id, null, Duty(_asha, "2026-05-11", "M", SupervisionRoles.ChiefConductor), Duty(_ravi, "2026-05-12", "M"), Duty(_ravi, "2026-05-11", "A"));
        Assert.Equal((0, 1, 1, 0), (more.Created, more.Updated, more.Unchanged, more.Removed));
        Assert.Equal("VIT/EXAM/2026-27/DUTY-002", (await _service.GetAsync(id)).Letters.Single(l => l.StaffId == _ravi.StaffId).LetterNo);
    }

    [Fact]
    public async Task A_new_letter_needs_the_letter_date_but_an_unchanged_chart_does_not()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;

        Assert.Equal("Enter the letter date.", await Refused(id, "", Duty(_asha, "2026-05-11", "M")));
        Assert.Contains("letter date is not a valid date", await Refused(id, "30/04/2026", Duty(_asha, "2026-05-11", "M")));
        Assert.Empty((await _service.GetAsync(id)).Entries);

        // A valuation day needs no letter, so no date.
        var result = await Save(id, "", Valuation(_asha, "2026-05-11"));
        Assert.Equal(0, result.Created);
        Assert.Single((await _service.GetAsync(id)).Entries);
    }

    [Fact]
    public async Task A_person_who_loses_every_duty_has_the_letter_cancelled_and_numbers_are_never_reused()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        await Save(id, "2026-04-30", Duty(_asha, "2026-05-11", "M"), Duty(_ravi, "2026-05-12", "M"));   // DUTY-001, DUTY-002

        // Ravi is cancelled (his duty becomes valuation), Meena is new.
        var result = await Save(id, "2026-05-02", Duty(_asha, "2026-05-11", "M"), Valuation(_ravi, "2026-05-12"), Duty(_meena, "2026-05-11", "A"));

        Assert.Equal((1, 0, 1, 1), (result.Created, result.Updated, result.Unchanged, result.Removed));
        var letters = (await _service.GetAsync(id)).Letters;
        Assert.Equal(new[] { "VIT/EXAM/2026-27/DUTY-001", "VIT/EXAM/2026-27/DUTY-003" }, letters.Select(l => l.LetterNo));
        var cancelled = await _context.StaffLetters.IgnoreQueryFilters().SingleAsync(l => l.StaffId == _ravi.StaffId);
        Assert.True(cancelled.IsDeleted);
        Assert.Equal("VIT/EXAM/2026-27/DUTY-002", cancelled.LetterNo);
        Assert.Equal(3, (await _service.GetAsync(id)).Entries.Count);   // Ravi's valuation entry stays

        // Ravi is given a duty again: a fresh letter, next number.
        await Save(id, "2026-05-03", Duty(_asha, "2026-05-11", "M"), Duty(_ravi, "2026-05-12", "M"), Duty(_meena, "2026-05-11", "A"));
        Assert.Equal("VIT/EXAM/2026-27/DUTY-004", (await _service.GetAsync(id)).Letters.Single(l => l.StaffId == _ravi.StaffId).LetterNo);

        // An empty chart cancels everything.
        var empty = await Save(id, null);
        Assert.Equal(3, empty.Removed);
        Assert.Empty((await _service.GetAsync(id)).Letters);
        Assert.Empty((await _service.GetAsync(id)).Entries);
    }

    [Fact]
    public async Task Duty_numbers_follow_the_colleges_prefix_and_run_apart_from_examiner_letters()
    {
        _context.CollegeSettings.Add(new CollegeSettings { Id = Guid.NewGuid(), CollegeId = _college, LetterPrefix = "VIVA/VIT/EXAM" });
        _context.StaffLetters.Add(new StaffLetter
        {
            Id = Guid.NewGuid(), CollegeId = _college, StaffId = _meena.StaffId, LetterType = StaffLetterTypes.Examiner, ExamId = _computerExam.ExamId,
            AYID = _ay, SerialNo = 5, LetterNo = "VIVA/VIT/EXAM/2026-27/EXM-005", LetterDate = new DateTime(2026, 4, 1), Role = "Examiner",
            StaffName = "Dr. Meena Shah", ExamTitle = "May 2026 Regular"
        });
        _context.SaveChanges();
        var chart = await NewChart();

        await Save(chart.Chart.Id, "2026-04-30", Duty(_asha, "2026-05-11", "M"));

        Assert.Equal("VIVA/VIT/EXAM/2026-27/DUTY-001", (await _service.GetAsync(chart.Chart.Id)).Letters.Single().LetterNo);
    }

    [Fact]
    public async Task Deleting_a_chart_cancels_its_letters_and_it_disappears()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        await Save(id, "2026-04-30", Duty(_asha, "2026-05-11", "M"), Duty(_ravi, "2026-05-12", "M"));

        Assert.Equal(2, await _service.DeleteAsync(id));

        Assert.Empty(await _service.ListAsync(_ay));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetAsync(id));
        Assert.Empty(await _context.StaffLetters.ToListAsync());
        var all = await _context.StaffLetters.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, all.Count);
        Assert.All(all, l => Assert.True(l.IsDeleted));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteAsync(id));

        // The numbers stay used: a later chart continues after them.
        var next = await NewChart();
        await Save(next.Chart.Id, "2026-05-30", Duty(_asha, "2026-05-11", "M"));
        Assert.Equal("VIT/EXAM/2026-27/DUTY-003", (await _service.GetAsync(next.Chart.Id)).Letters.Single().LetterNo);
    }

    [Fact]
    public async Task Another_colleges_charts_and_staff_are_invisible()
    {
        var chart = await NewChart();
        var theirs = new DutyChart { Id = Guid.NewGuid(), CollegeId = _otherCollege, AYID = _ay, Name = "Their chart", FromDate = new DateTime(2026, 5, 1), ToDate = new DateTime(2026, 5, 5) };
        _context.DutyCharts.Add(theirs);
        var theirStaff = Person("E900", "Mr.", "Their", null, "Person", "their@other.edu", college: _otherCollege);
        _context.SaveChanges();

        Assert.Equal(chart.Chart.Id, Assert.Single(await _service.ListAsync(_ay)).Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetAsync(theirs.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteAsync(theirs.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SaveEntriesAsync(theirs.Id, new DutyChartEntriesSave()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ExcelAsync(theirs.Id));
        var message = await Refused(chart.Chart.Id, "2026-04-30", Duty(theirStaff, "2026-05-11", "M"));
        Assert.Contains("not found", message);
    }

    // ------------------------------------------------------------------ Excel

    private static IFormFile File(byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "File", "chart.xlsx");
    }

    /// <summary>The downloaded sheet with some cells changed by the test.</summary>
    private async Task<IFormFile> EditedSheet(Guid chartId, Action<IXLWorksheet> edit)
    {
        var (bytes, _) = await _service.ExcelAsync(chartId);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        edit(workbook.Worksheet(1));
        var output = new MemoryStream();
        workbook.SaveAs(output);
        return File(output.ToArray());
    }

    private static int RowOf(IXLWorksheet sheet, string employeeId)
    {
        for (int r = 4; r <= sheet.LastRowUsed()!.RowNumber(); r++)
            if (sheet.Cell(r, 2).GetString() == employeeId) return r;
        throw new InvalidOperationException("no row for " + employeeId);
    }

    private static int ColumnOf(IXLWorksheet sheet, string dayHeader)
    {
        for (int c = 4; c <= sheet.Row(3).LastCellUsed()!.Address.ColumnNumber; c++)
            if (sheet.Cell(3, c).GetString().StartsWith(dayHeader)) return c;
        throw new InvalidOperationException("no column for " + dayHeader);
    }

    private Task<StaffImportResult> Import(Guid chartId, IFormFile file, bool preview = true, string? letterDate = "2026-04-30") =>
        _service.ImportAsync(chartId, new DutyChartImportRequest { File = file, Preview = preview, LetterDate = letterDate });

    [Fact]
    public async Task The_excel_sheet_lists_all_active_internal_staff_by_group_with_the_codes_and_a_legend()
    {
        var chart = await NewChart();
        Person("E105", "Mr.", "Gone", null, "Away", "gone@viva.edu", active: false);
        Person("", "Dr.", "Neha", null, "Iyer", "neha@other.edu", external: true);
        _context.SaveChanges();
        await Save(chart.Chart.Id, "2026-04-30",
            Duty(_ravi, "2026-05-11", "MA", SupervisionRoles.ChiefConductor), Duty(_ravi, "2026-05-12", "M", SupervisionRoles.Senior),
            Duty(_asha, "2026-05-11", "A"), Valuation(_asha, "2026-05-12"), Other(_meena, "2026-05-13", "Exam cell"), Other(_meena, "2026-05-11", null),
            Duty(_om, "2026-05-13", "A"));

        var (bytes, name) = await _service.ExcelAsync(chart.Chart.Id);

        Assert.Equal("Duty Chart May 2026 End-Semester.xlsx", name);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        Assert.Equal("Duty Chart — May 2026 End-Semester (11 May 2026 to 15 May 2026)", sheet.Cell(1, 1).GetString());
        Assert.Equal("Be on campus for 8 hours", sheet.Cell(2, 1).GetString());
        Assert.Equal(new[] { "Sr. No.", "Employee ID", "Employee Name", "11-05-2026\nMon", "12-05-2026\nTue", "13-05-2026\nWed", "Duties" },
            Enumerable.Range(1, 7).Select(c => sheet.Cell(3, c).GetString()));

        // Groups in order, each person once, ordered by first name inside a group.
        Assert.Equal("Chief Conductor / Senior Supervisors", sheet.Cell(4, 1).GetString());
        Assert.Equal(("E102", "Dr. Ravi Kumar"), (sheet.Cell(5, 2).GetString(), sheet.Cell(5, 3).GetString()));
        Assert.Equal("Teaching staff", sheet.Cell(6, 1).GetString());
        Assert.Equal(new[] { "E101", "E103" }, new[] { sheet.Cell(7, 2).GetString(), sheet.Cell(8, 2).GetString() });
        Assert.Equal("Non-teaching staff", sheet.Cell(9, 1).GetString());
        Assert.Equal("E104", sheet.Cell(10, 2).GetString());
        Assert.Equal(1, sheet.Cell(5, 1).GetValue<int>());
        Assert.Equal(4, sheet.Cell(10, 1).GetValue<int>());

        // Codes.
        Assert.Equal(new[] { "CC (M/A)", "SS (M)", "" }, Enumerable.Range(4, 3).Select(c => sheet.Cell(5, c).GetString()));
        Assert.Equal(3, sheet.Cell(5, 7).GetValue<int>());   // sessions
        Assert.Equal(new[] { "A", "V", "" }, Enumerable.Range(4, 3).Select(c => sheet.Cell(7, c).GetString()));
        Assert.Equal(new[] { "O", "", "Exam cell" }, Enumerable.Range(4, 3).Select(c => sheet.Cell(8, c).GetString()));
        Assert.Equal(1, sheet.Cell(7, 7).GetValue<int>());
        Assert.Equal(0, sheet.Cell(8, 7).GetValue<int>());
        Assert.Equal("A", sheet.Cell(10, 6).GetString());   // non-teaching: no prefix

        // Nobody else, a legend with the day times at the end.
        var ids = Enumerable.Range(4, sheet.LastRowUsed()!.RowNumber()).Select(r => sheet.Cell(r, 2).GetString()).Where(i => i != "").ToList();
        Assert.Equal(new[] { "E102", "E101", "E103", "E104" }, ids);
        Assert.Contains(Enumerable.Range(4, sheet.LastRowUsed()!.RowNumber()), r => sheet.Cell(r, 1).GetString() == "Legend");
        var legendRow = Enumerable.Range(4, sheet.LastRowUsed()!.RowNumber()).First(r => sheet.Cell(r, 3).GetString() == "Morning");
        Assert.Equal(Morning, sheet.Cell(legendRow, 4).GetString());
        Assert.Equal(Afternoon, sheet.Cell(legendRow + 1, 6).GetString());
    }

    [Fact]
    public async Task The_excel_sheet_round_trips_to_the_same_entries_and_letters()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        await Save(id, "2026-04-30",
            Duty(_ravi, "2026-05-11", "MA", SupervisionRoles.ChiefConductor), Duty(_ravi, "2026-05-12", "M", SupervisionRoles.Senior),
            Duty(_asha, "2026-05-11", "A"), Valuation(_asha, "2026-05-12"), Other(_meena, "2026-05-13", "Exam cell"), Other(_meena, "2026-05-11", null),
            Duty(_om, "2026-05-13", "A"), Duty(_asha, "2026-05-13", "A"));
        var before = await _service.GetAsync(id);
        var (bytes, _) = await _service.ExcelAsync(id);

        // The untouched sheet changes nothing.
        var preview = await Import(id, File(bytes));
        Assert.True(preview.Success);
        Assert.Equal(new[] { "Unchanged" }, preview.Rows.Select(r => r.Action).Distinct());
        Assert.Equal(4, preview.Rows.Count);
        Assert.Equal("Ready: 0 new, 0 updated, 4 unchanged. Check the list, then press Save.", preview.Message);
        var unchanged = await Import(id, File(bytes), preview: false);
        Assert.True(unchanged.Saved);
        Assert.Equal("Saved: 0 new, 0 updated, 3 unchanged, 0 cancelled.", unchanged.Message);

        // Wipe the chart, then import the downloaded sheet: the same entries come back.
        await Save(id, null);
        Assert.Empty((await _service.GetAsync(id)).Entries);
        var restored = await Import(id, File(bytes), preview: false, letterDate: "2026-05-01");
        Assert.True(restored.Saved);
        Assert.Equal(new[] { "New" }, restored.Rows.Select(r => r.Action).Distinct());

        var after = await _service.GetAsync(id);
        Assert.Equal(Sig(before.Entries), Sig(after.Entries));
        Assert.Equal(3, after.Letters.Count);
        Assert.All(after.Letters, l => Assert.Equal("2026-05-01", l.LetterDate));
        Assert.Equal(before.Letters.Select(l => (l.StaffId, l.Role, string.Join(";", l.Lines.Select(x => $"{x.Date}/{x.Time}/{x.Role}")))).OrderBy(x => x.StaffId),
            after.Letters.Select(l => (l.StaffId, l.Role, string.Join(";", l.Lines.Select(x => $"{x.Date}/{x.Time}/{x.Role}")))).OrderBy(x => x.StaffId));
    }

    [Fact]
    public async Task Import_replaces_the_chart_and_the_preview_says_who_loses_their_duties()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        await Save(id, "2026-04-30", Duty(_asha, "2026-05-11", "M"), Duty(_ravi, "2026-05-12", "M"), Duty(_om, "2026-05-11", "M"));
        var file = await EditedSheet(id, sheet =>
        {
            sheet.Cell(RowOf(sheet, "E101"), ColumnOf(sheet, "11-05")).Value = "M/A";   // changed
            sheet.Cell(RowOf(sheet, "E103"), ColumnOf(sheet, "12-05")).Value = "ss (m)";
            sheet.Cell(RowOf(sheet, "E102"), ColumnOf(sheet, "12-05")).Value = "";      // Ravi's only duty removed
            sheet.Cell(RowOf(sheet, "E104"), ColumnOf(sheet, "11-05")).Value = "";      // Om too
        });

        var preview = await Import(id, file);

        Assert.True(preview.Success);
        Assert.Equal(new[] { ("Dr. Meena Shah", "New"), ("Dr. Ravi Kumar", "Update"), ("Mr. Om Das", "Update"), ("Prof. Asha R. Patil", "Update") },
            preview.Rows.OrderBy(r => r.Name).Select(r => (r.Name, r.Action)));
        Assert.Equal("All duties are removed.", preview.Rows.Single(r => r.Name == "Dr. Ravi Kumar").Note);
        Assert.Equal("Ready: 1 new, 3 updated, 0 unchanged. Check the list, then press Save.", preview.Message);
        Assert.Equal(3, (await _service.GetAsync(id)).Entries.Count);   // a preview saves nothing

        var saved = await Import(id, file, preview: false);
        Assert.True(saved.Saved);
        var after = await _service.GetAsync(id);
        Assert.Equal(new[] { ("2026-05-11", "MA", SupervisionRoles.Junior), ("2026-05-12", "M", SupervisionRoles.Senior) },
            after.Entries.Select(e => (e.Date, e.Session!, e.Role!)).OrderBy(e => e.Item1));
        Assert.Equal(new[] { _asha.StaffId, _meena.StaffId }.OrderBy(i => i), after.Letters.Select(l => l.StaffId).OrderBy(i => i));
        Assert.Equal(SupervisionRoles.Senior, after.Letters.Single(l => l.StaffId == _meena.StaffId).Role);
    }

    [Fact]
    public async Task People_missing_from_the_sheet_lose_their_duties()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        await Save(id, "2026-04-30", Duty(_asha, "2026-05-11", "M"), Duty(_ravi, "2026-05-12", "M"));
        var file = await EditedSheet(id, sheet =>
        {
            sheet.Row(RowOf(sheet, "E102")).Delete();   // Ravi's row is gone from the file
        });

        var preview = await Import(id, file);
        Assert.Equal("Ready: 0 new, 0 updated, 1 unchanged. 1 person not in the sheet will lose their duties. Check the list, then press Save.", preview.Message);

        var saved = await Import(id, file, preview: false);
        Assert.StartsWith("Saved: 0 new, 0 updated, 1 unchanged, 1 cancelled.", saved.Message);
        Assert.Equal(new[] { _asha.StaffId }, (await _service.GetAsync(id)).Entries.Select(e => e.StaffId));
    }

    [Fact]
    public async Task Import_reports_unknown_people_days_sessions_and_unreadable_codes_and_saves_nothing()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        await Save(id, "2026-04-30", Duty(_meena, "2026-05-11", "M"));
        var file = await EditedSheet(id, sheet =>
        {
            var asha = RowOf(sheet, "E101");
            sheet.Cell(asha, ColumnOf(sheet, "11-05")).Value = "M";
            sheet.Cell(asha, 2).Value = "E999";                                                  // not in Staff Master
            sheet.Cell(RowOf(sheet, "E102"), ColumnOf(sheet, "13-05")).Value = "M";              // no morning on Wednesday
            sheet.Cell(RowOf(sheet, "E102"), ColumnOf(sheet, "12-05")).Value = "A";              // no afternoon on Tuesday
            sheet.Cell(RowOf(sheet, "E103"), ColumnOf(sheet, "12-05")).Value = "CC (X)";         // unreadable
            sheet.Cell(RowOf(sheet, "E104"), ColumnOf(sheet, "11-05")).Value = new string('x', 101);   // note too long
            // A Thursday column the chart does not have.
            sheet.Cell(3, 8).Value = "14-05-2026\nThu";
            sheet.Cell(RowOf(sheet, "E104"), 8).Value = "M";
        });

        var result = await Import(id, file, preview: false);

        Assert.False(result.Success);
        Assert.False(result.Saved);
        Assert.StartsWith("Nothing was imported. Fix", result.Message);
        Assert.Equal(4, result.Rows.Count(r => r.HasErrors));
        Assert.Contains(result.Errors, e => e.Contains("Employee ID E999 is not in Staff Master"));
        Assert.Contains(result.Errors, e => e.Contains("13-05-2026: there is no morning session that day"));
        Assert.Contains(result.Errors, e => e.Contains("12-05-2026: there is no afternoon session that day"));
        Assert.Contains(result.Errors, e => e.Contains("cannot read \"CC (X)\""));
        Assert.Contains(result.Errors, e => e.Contains("14-05-2026 is not a day of this chart; add it under Days"));
        Assert.Contains(result.Errors, e => e.Contains("longer than 100 characters"));
        Assert.All(result.Rows.Where(r => r.HasErrors), r => Assert.Equal("", r.Action));
        Assert.Equal(new[] { _meena.StaffId }, (await _service.GetAsync(id)).Entries.Select(e => e.StaffId));   // nothing saved
    }

    [Fact]
    public async Task A_wrong_name_is_only_a_note_and_a_file_without_headings_is_refused()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        var file = await EditedSheet(id, sheet =>
        {
            sheet.Cell(RowOf(sheet, "E101"), 3).Value = "Someone Else";
            sheet.Cell(RowOf(sheet, "E101"), ColumnOf(sheet, "11-05")).Value = "m";
            sheet.Cell(RowOf(sheet, "E102"), 3).Value = "dr ravi KUMAR";   // same name, other spelling: no note
            sheet.Cell(RowOf(sheet, "E102"), ColumnOf(sheet, "11-05")).Value = "-";   // a dash is empty
        });

        var preview = await Import(id, file);

        Assert.True(preview.Success);
        var row = Assert.Single(preview.Rows);
        Assert.Equal(("Prof. Asha R. Patil", "New", false), (row.Name, row.Action, row.HasErrors));
        Assert.Contains("differs from Staff Master", row.Note);

        var empty = new XLWorkbook();
        empty.AddWorksheet("Sheet1").Cell(1, 1).Value = "Names";
        var stream = new MemoryStream();
        empty.SaveAs(stream);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Import(id, File(stream.ToArray())));
        Assert.Contains("Employee ID", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ImportAsync(id, new DutyChartImportRequest()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Import(id, File(new byte[] { 1, 2, 3 })));
        Assert.Contains("letter date", (await Assert.ThrowsAsync<InvalidOperationException>(() => Import(id, file, letterDate: "yesterday"))).Message);
    }

    [Fact]
    public async Task Import_reads_dates_in_the_heading_whatever_their_spelling()
    {
        var chart = await NewChart();
        var id = chart.Chart.Id;
        var file = await EditedSheet(id, sheet =>
        {
            sheet.Cell(3, ColumnOf(sheet, "11-05")).Value = new DateTime(2026, 5, 11);           // a real Excel date
            sheet.Cell(3, ColumnOf(sheet, "12-05")).Value = "12 May 2026\nTuesday";
            sheet.Cell(3, ColumnOf(sheet, "13-05")).Value = "2026-05-13";
            foreach (var c in new[] { 4, 5, 6 }) sheet.Cell(RowOf(sheet, "E101"), c).Value = "A";
            sheet.Cell(RowOf(sheet, "E101"), 4).Value = "M";
            sheet.Cell(RowOf(sheet, "E101"), 5).Value = "M";
        });

        var saved = await Import(id, file, preview: false);

        Assert.True(saved.Saved);
        Assert.Equal(new[] { ("2026-05-11", "M"), ("2026-05-12", "M"), ("2026-05-13", "A") },
            (await _service.GetAsync(id)).Entries.OrderBy(e => e.Date).Select(e => (e.Date, e.Session!)));
    }

    // ------------------------------------------------------------------ reading codes

    [Theory]
    [InlineData("M", "M", null)]
    [InlineData(" m ", "M", null)]
    [InlineData("A", "A", null)]
    [InlineData("M/A", "MA", null)]
    [InlineData("m / a", "MA", null)]
    [InlineData("MA", "MA", null)]
    [InlineData("AM", "M", null)]
    [InlineData("PM", "A", null)]
    [InlineData("AM/PM", "MA", null)]
    [InlineData("CC (M/A)", "MA", "Chief Conductor")]
    [InlineData("cc(m)", "M", "Chief Conductor")]
    [InlineData("CC M", "M", "Chief Conductor")]
    [InlineData("SS (A)", "A", "Senior Supervisor")]
    [InlineData("ss-a", "A", "Senior Supervisor")]
    [InlineData("A (SS)", "A", "Senior Supervisor")]
    [InlineData("M/A [CC]", "MA", "Chief Conductor")]
    [InlineData("M/A CC", "MA", "Chief Conductor")]
    public void Duty_codes_are_read_tolerantly(string text, string session, string? role)
    {
        var code = DutyCodes.Parse(text, out var error);

        Assert.Null(error);
        Assert.Equal(("duty", session, role, null), (code!.Kind, code.Session, code.Role, code.Note));
    }

    [Theory]
    [InlineData("V")]
    [InlineData(" v ")]
    [InlineData("Valuation")]
    public void V_is_valuation(string text)
    {
        var code = DutyCodes.Parse(text, out var error);

        Assert.Null(error);
        Assert.Equal(("valuation", null, null, null), (code!.Kind, code.Session, code.Role, code.Note));
    }

    [Theory]
    [InlineData("Exam cell", "Exam cell")]
    [InlineData("  Exam   cell ", "Exam cell")]
    [InlineData("Paper setting", "Paper setting")]
    public void Other_text_is_other_work_with_that_note(string text, string note)
    {
        var code = DutyCodes.Parse(text, out var error);

        Assert.Null(error);
        Assert.Equal(("other", null, null, note), (code!.Kind, code.Session, code.Role, code.Note));
    }

    [Fact]
    public void O_is_other_work_without_a_note_and_empty_cells_are_nothing()
    {
        Assert.Equal(("other", null), (DutyCodes.Parse("O", out _)!.Kind, DutyCodes.Parse("O", out _)!.Note));
        foreach (var empty in new[] { "", "  ", "-", "–", null })
        {
            Assert.Null(DutyCodes.Parse(empty, out var error));
            Assert.Null(error);
        }
    }

    [Theory]
    [InlineData("CC")]
    [InlineData("SS X")]
    [InlineData("CC V")]
    public void A_role_without_a_session_cannot_be_read(string text)
    {
        var code = DutyCodes.Parse(text, out var error);

        Assert.Null(code);
        Assert.Contains("cannot read", error);
    }
}
