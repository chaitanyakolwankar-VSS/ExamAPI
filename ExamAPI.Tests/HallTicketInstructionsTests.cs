using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.GenerateHallTicket;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// Owner decision 2026-11-02: every college prints "Instructions to candidates" on its hall tickets by default;
/// the college admin can switch them off or write their own lines.
/// </summary>
public sealed class HallTicketInstructionsTests
{
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _otherCollege = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly GenerateHallTicketService _service;

    public HallTicketInstructionsTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new GenerateHallTicketService(_context);
        _context.Colleges.Add(new College { CollegeId = _college, Name = "VIVA Institute of Technology", CollegeCode = "VIT", CollegeCenter = "Virar", ContactEmail = "office@viva.edu", ContactPhone = "0250" });
        _context.Colleges.Add(new College { CollegeId = _otherCollege, Name = "Other College", CollegeCode = "OTH", CollegeCenter = "Pune", ContactEmail = "o@other.edu", ContactPhone = "020" });
        _context.SaveChanges();
    }

    private CollegeSettings Settings(Guid? college = null, bool off = false, string? text = null)
    {
        var s = new CollegeSettings { Id = Guid.NewGuid(), CollegeId = college ?? _college, HallTicketInstructionsOff = off, HallTicketInstructions = text };
        _context.CollegeSettings.Add(s);
        _context.SaveChanges();
        return s;
    }

    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => "Rule " + i));

    [Fact]
    public async Task Without_a_settings_row_the_hall_ticket_prints_the_defaults()
    {
        var college = await _service.HallTicketCollegeData();
        Assert.Equal(HallTicketInstructionDefaults.Lines, college.Instructions);
        Assert.Equal(6, college.Instructions.Count);

        var dto = await _service.GetInstructionsAsync();
        Assert.True(dto.On);
        Assert.False(dto.Custom);
        Assert.Equal(HallTicketInstructionDefaults.Lines, dto.Lines);
        Assert.Equal(HallTicketInstructionDefaults.Lines, dto.DefaultLines);
        Assert.Equal(10, dto.MaxLines);
        Assert.Equal(250, dto.MaxLineLength);
        Assert.Equal(1500, dto.MaxLength);
    }

    [Fact]
    public async Task A_settings_row_without_text_still_prints_the_defaults()
    {
        Settings(text: null);
        Assert.Equal(HallTicketInstructionDefaults.Lines, (await _service.HallTicketCollegeData()).Instructions);
        Assert.False((await _service.GetInstructionsAsync()).Custom);
    }

    [Fact]
    public async Task Switched_off_prints_nothing_but_the_editor_keeps_the_text()
    {
        Settings(off: true, text: "Bring your ID card.");
        Assert.Empty((await _service.HallTicketCollegeData()).Instructions);

        var dto = await _service.GetInstructionsAsync();
        Assert.False(dto.On);
        Assert.True(dto.Custom);
        Assert.Equal(new[] { "Bring your ID card." }, dto.Lines);

        // Off with no text of its own: the editor still shows the defaults.
        var none = await _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = false, Text = "" });
        Assert.False(none.On);
        Assert.Equal(HallTicketInstructionDefaults.Lines, none.Lines);
        Assert.Empty((await _service.HallTicketCollegeData()).Instructions);
    }

    [Fact]
    public async Task Own_text_is_saved_trimmed_without_blank_lines_and_printed()
    {
        var saved = await _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = true, Text = "  Be on time. \r\n\r\n  Carry this hall ticket.  \n \n" });
        Assert.True(saved.On);
        Assert.True(saved.Custom);
        Assert.Equal(new[] { "Be on time.", "Carry this hall ticket." }, saved.Lines);
        Assert.Equal(HallTicketInstructionDefaults.Lines, saved.DefaultLines);

        Assert.Equal("Be on time.\nCarry this hall ticket.", (await _context.CollegeSettings.SingleAsync()).HallTicketInstructions);
        Assert.Equal(new[] { "Be on time.", "Carry this hall ticket." }, (await _service.HallTicketCollegeData()).Instructions);
        Assert.Contains("Shown: yes; text: 2 line(s)", (await _context.AuditLogs.SingleAsync(a => a.Event == "HallTicketInstructionsChanged")).Details);
    }

    [Fact]
    public async Task Text_equal_to_the_defaults_is_stored_as_nothing()
    {
        Settings(text: "Old line.");
        var saved = await _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = true, Text = " " + string.Join("\r\n\r\n", HallTicketInstructionDefaults.Lines) + "\n" });
        Assert.False(saved.Custom);
        Assert.Null((await _context.CollegeSettings.SingleAsync()).HallTicketInstructions);

        // Empty text goes back to the defaults too.
        await _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = true, Text = "Something." });
        var cleared = await _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = true, Text = "  \n " });
        Assert.False(cleared.Custom);
        Assert.Null((await _context.CollegeSettings.SingleAsync()).HallTicketInstructions);
        Assert.Contains("text: default", (await _context.AuditLogs.Where(a => a.Event == "HallTicketInstructionsChanged").OrderByDescending(a => a.Timestamp).FirstAsync()).Details);
    }

    [Fact]
    public async Task Saving_creates_the_settings_row_when_missing_and_keeps_other_settings()
    {
        Assert.Empty(await _context.CollegeSettings.ToListAsync());
        await _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = false });
        var row = await _context.CollegeSettings.SingleAsync();
        Assert.True(row.HallTicketInstructionsOff);
        Assert.Equal(_college, row.CollegeId);

        row.LetterPrefix = "VIVA";
        await _context.SaveChangesAsync();
        await _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = true, Text = "One." });
        Assert.Equal("VIVA", (await _context.CollegeSettings.SingleAsync()).LetterPrefix);
    }

    [Fact]
    public async Task Too_many_lines_a_long_line_or_a_long_text_are_refused()
    {
        var many = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = true, Text = Lines(11) }));
        Assert.Contains("more than 10", many.Message);
        var longLine = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = true, Text = new string('A', 251) }));
        Assert.Contains("250", longLine.Message);
        var total = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveInstructionsAsync(new HallTicketInstructionsSave
        {
            On = true, Text = string.Join("\n", Enumerable.Range(1, 7).Select(_ => new string('B', 240)))
        }));
        Assert.Contains("1500", total.Message);
        Assert.Empty(await _context.CollegeSettings.ToListAsync());

        // The limits themselves are fine.
        var ok = await _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = true, Text = string.Join("\n", Enumerable.Range(1, 10).Select(_ => new string('C', 140))) });
        Assert.Equal(10, ok.Lines.Count);
        var exact = await _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = true, Text = new string('D', 250) });
        Assert.Equal(250, exact.Lines.Single().Length);
    }

    [Fact]
    public async Task A_refused_save_changes_nothing()
    {
        Settings(text: "Keep me.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = false, Text = Lines(11) }));
        var row = await _context.CollegeSettings.AsNoTracking().SingleAsync();
        Assert.False(row.HallTicketInstructionsOff);
        Assert.Equal("Keep me.", row.HallTicketInstructions);
        Assert.Empty(await _context.AuditLogs.Where(a => a.Event == "HallTicketInstructionsChanged").ToListAsync());
    }

    [Fact]
    public async Task Another_colleges_settings_are_never_used()
    {
        Settings(_otherCollege, off: true, text: "Their rule.");

        Assert.Equal(HallTicketInstructionDefaults.Lines, (await _service.HallTicketCollegeData()).Instructions);
        var dto = await _service.GetInstructionsAsync();
        Assert.True(dto.On);
        Assert.False(dto.Custom);

        // Saving makes our own row and leaves theirs alone.
        await _service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = true, Text = "Our rule." });
        var theirs = await _context.CollegeSettings.IgnoreQueryFilters().SingleAsync(s => s.CollegeId == _otherCollege);
        Assert.True(theirs.HallTicketInstructionsOff);
        Assert.Equal("Their rule.", theirs.HallTicketInstructions);
        Assert.Equal(new[] { "Our rule." }, (await _service.HallTicketCollegeData()).Instructions);
    }

    [Fact]
    public async Task Saving_needs_a_college()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var platform = new Mock<ICurrentUser>();
        platform.SetupGet(u => u.CollegeId).Returns((Guid?)null);
        var service = new GenerateHallTicketService(new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, platform.Object));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveInstructionsAsync(new HallTicketInstructionsSave { On = true, Text = "Rule." }));
        Assert.Equal("Hall ticket instructions belong to a college: sign in as that college's admin.", ex.Message);
    }
}
