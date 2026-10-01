using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Files;
using ExamAPI.Services.Report;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using ExamAPI.DTOs;
using ExamAPI.Services.Report.Documents;
using OfficeOpenXml;
using QuestPDF.Fluent;

namespace ExamAPI.Tests;

/// <summary>PLAN-04 / DEC-14: every report header uses College Details, with a text fallback.</summary>
public sealed class CollegeBrandingTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "branding-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FileStorage _storage;
    private readonly Guid _collegeId = Guid.NewGuid();
    private readonly ApplicationDbContext _context;

    public CollegeBrandingTests()
    {
        Directory.CreateDirectory(_base);
        _storage = new FileStorage(_base, null, Path.Combine(_base, "store"));
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_collegeId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("Viva College", "VC", "Viva College")]
    [InlineData("  Viva College ", "VC", "Viva College")]
    [InlineData("", "VC", "VC")]
    [InlineData(null, "  VC ", "VC")]
    [InlineData("  ", null, "")]
    [InlineData(null, null, "")]
    public void Display_name_falls_back_to_code_then_empty_and_never_prints_a_not_found_placeholder(string? name, string? code, string expected)
    {
        var result = CollegeBranding.DisplayName(name, code);
        Assert.Equal(expected, result);
        Assert.DoesNotContain("Not Found", result, StringComparison.OrdinalIgnoreCase);
    }

    private void AddCollege(string name, string? logo)
    {
        _context.Colleges.Add(new College
        {
            CollegeId = _collegeId, Name = name, CollegeCode = "VC", CollegeCenter = "Main",
            ContactEmail = "a@b.c", ContactPhone = "1", Address = "  12 Main Road ", LogoUrl = logo
        });
        _context.SaveChanges();
    }

    [Fact]
    public async Task Load_returns_name_address_and_logo_bytes_when_the_logo_exists()
    {
        var stored = await _storage.SaveAsync(UploadsTests.Png, FileStorage.CollegeLogosFolder, "logo.png");
        AddCollege("Viva College", stored);

        var branding = await CollegeBranding.LoadAsync(_context, _storage, _collegeId);

        Assert.Equal("Viva College", branding.Name);
        Assert.Equal("12 Main Road", branding.Address);
        Assert.True(branding.HasLogo);
        Assert.Equal(UploadsTests.Png, branding.Logo);
    }

    [Fact]
    public async Task Load_falls_back_to_text_only_when_the_logo_is_missing_unreadable_or_not_configured()
    {
        AddCollege("Viva College", "/Clg_details/logos/gone.png");
        Assert.False((await CollegeBranding.LoadAsync(_context, _storage, _collegeId)).HasLogo);
        Assert.False((await CollegeBranding.LoadAsync(_context, null, _collegeId)).HasLogo);

        // Present but not an image: must degrade to the text header, not fail the report.
        await _storage.SaveAsync(Enumerable.Range(1, 40).Select(i => (byte)i).ToArray(),
            FileStorage.CollegeLogosFolder, "corrupt.png");
        var college = _context.Colleges.Single();
        college.LogoUrl = "college/logos/corrupt.png";
        _context.SaveChanges();

        var branding = await CollegeBranding.LoadAsync(_context, _storage, _collegeId);
        Assert.False(branding.HasLogo);
        Assert.Equal("Viva College", branding.Name);
    }

    [Fact]
    public async Task Load_for_an_unknown_college_gives_empty_branding_not_a_placeholder()
    {
        var branding = await CollegeBranding.LoadAsync(_context, _storage, Guid.NewGuid());
        Assert.Equal(string.Empty, branding.Name);
        Assert.Null(branding.Logo);
    }

    [Fact]
    public void Excel_logo_is_a_floating_picture_that_moves_no_cells()
    {
        ExcelPackage.License.SetNonCommercialPersonal("ReactApi Project");
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("S");
        sheet.Cells[1, 1].Value = "College";
        sheet.Cells[8, 3].Value = "row8col3";
        sheet.Cells[8, 9].Value = "row8col9";

        Assert.True(ExcelBranding.TryAddLogo(sheet, UploadsTests.Png, 1, 1, 96, 44));
        Assert.Single(sheet.Drawings);
        Assert.Equal("College", sheet.Cells[1, 1].Text);
        Assert.Equal("row8col3", sheet.Cells[8, 3].Text);
        Assert.Equal("row8col9", sheet.Cells[8, 9].Text);

        // No logo / garbage: nothing added, no exception.
        Assert.False(ExcelBranding.TryAddLogo(sheet, null));
        Assert.False(ExcelBranding.TryAddLogo(sheet, new byte[] { 1, 2, 3 }));
        Assert.Single(sheet.Drawings);
    }

    [Fact]
    public void Image_size_is_read_from_the_header()
    {
        Assert.True(ImageSize.TryRead(UploadsTests.Png, out var w, out var h));
        Assert.Equal((1, 1), (w, h));
        Assert.False(ImageSize.TryRead(new byte[40], out _, out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pdf_headers_render_with_and_without_a_logo(bool withLogo)
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        byte[]? logo = withLogo ? UploadsTests.Png : null;

        var marksheet = new MarksheetReportDto { CollegeName = "Viva College", CollegeLogo = logo, StudentName = "A B" };
        var gazette = new GazetteReportDto { CollegeName = "Viva College", CollegeLogo = logo };

        foreach (var pdf in new[]
        {
            new MarksheetDocument(marksheet).GeneratePdf(),
            new BulkMarksheetDocument(new[] { marksheet, marksheet }).GeneratePdf(),
            new GazetteDocument(gazette, new GazetteRequestDto()).GeneratePdf()
        })
        {
            Assert.True(pdf.Length > 100);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }
    }
}
