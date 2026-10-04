using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Files;
using ExamAPI.Services.StudentMasters;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ExamAPI.Tests;

/// <summary>Student photo + signature upload (hall tickets print both): images only, own college only, old file replaced.</summary>
public sealed class StudentImagesTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "student-images-" + Guid.NewGuid().ToString("N"));
    private readonly FileStorage _storage;
    private readonly ApplicationDbContext _context;
    private readonly StudentMasterService _service;
    private readonly Guid _college = Guid.NewGuid();
    private static readonly string PngDataUrl = "data:image/png;base64," + Convert.ToBase64String(UploadsTests.Png);

    public StudentImagesTests()
    {
        Directory.CreateDirectory(_base);
        _storage = new FileStorage(_base, null, Path.Combine(_base, "store"));
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.CollegeId).Returns(_college);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, user.Object);
        _service = new StudentMasterService(_context, _storage);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    private StudentMaster Student(string id, Guid? college = null)
    {
        var s = new StudentMaster { StdMstId = Guid.NewGuid(), StudentId = id, FirstName = "A", LastName = "B", CollegeId = college ?? _college };
        _context.StudentMasters.Add(s);
        _context.SaveChanges();
        return s;
    }

    [Fact]
    public async Task Photo_and_signature_are_stored_and_replacing_one_deletes_the_old_file_and_keeps_the_other()
    {
        var s = Student("PH1");

        var (photo, sign) = await _service.UpdateImagesAsync(new StudentImagesDto { StudentId = "PH1", Photo = PngDataUrl, Sign = PngDataUrl });
        Assert.NotNull(_storage.ResolveExisting(photo));
        Assert.NotNull(_storage.ResolveExisting(sign));

        var (photo2, sign2) = await _service.UpdateImagesAsync(new StudentImagesDto { StudentId = "PH1", Sign = PngDataUrl });
        Assert.Equal(photo, photo2);
        Assert.NotEqual(sign, sign2);
        Assert.Null(_storage.ResolveExisting(sign));
        Assert.Equal("A", s.FirstName);
    }

    [Fact]
    public async Task Non_images_and_other_colleges_students_are_refused()
    {
        Student("PH2");
        Student("EN1", Guid.NewGuid());
        var notAnImage = "data:image/png;base64," + Convert.ToBase64String("not an image"u8.ToArray());

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateImagesAsync(new StudentImagesDto { StudentId = "PH2", Photo = notAnImage }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateImagesAsync(new StudentImagesDto { StudentId = "PH2", Sign = "data:image/png;base64,%%%" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateImagesAsync(new StudentImagesDto { StudentId = "EN1", Photo = PngDataUrl }));
        Assert.Null(_context.StudentMasters.Single(s => s.StudentId == "PH2").PhotoUrl);
    }
}
