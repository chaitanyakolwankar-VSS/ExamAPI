using ExamAPI.Controllers;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Files;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ExamAPI.Tests;

/// <summary>DB-07 / DEC-12: uploads live outside wwwroot and are only readable through an authorised, tenant-checked endpoint.</summary>
public sealed class UploadsTests : IDisposable
{
    // 1x1 PNG.
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _base = Path.Combine(Path.GetTempPath(), "uploads-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _content;
    private readonly string _wwwroot;
    private readonly string _root;
    private readonly FileStorage _storage;

    public UploadsTests()
    {
        _content = Path.Combine(_base, "content");
        _wwwroot = Path.Combine(_content, "wwwroot");
        _root = Path.Combine(_base, "persistent");
        Directory.CreateDirectory(_wwwroot);
        _storage = new FileStorage(_content, _wwwroot, _root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    [Fact]
    public void Default_root_is_App_Data_outside_wwwroot()
    {
        var storage = new FileStorage(_content, _wwwroot, null);
        Assert.Equal(Path.Combine(_content, "App_Data", "uploads"), storage.Root);
        Assert.DoesNotContain("wwwroot", storage.Root);
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("students/../../secret.png")]
    [InlineData("..\\secret.png")]
    [InlineData("students\\..\\..\\secret.png")]
    [InlineData("/../secret.png")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("students/a.png\0.txt")]
    [InlineData("students//a.png")]
    [InlineData("students/./a.png")]
    [InlineData("secret.png:stream")]
    [InlineData("students/*.png")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public void Traversal_and_malformed_paths_are_rejected(string path)
    {
        // Real files the traversal would reach if it worked.
        File.WriteAllBytes(Path.Combine(_base, "secret.png"), Png);
        File.WriteAllBytes(Path.Combine(_content, "secret.png"), Png);

        Assert.Null(FileStorage.NormalizeKey(path));
        Assert.Null(_storage.ResolveExisting(path));
    }

    [Fact]
    public async Task Save_rejects_a_file_name_that_escapes_the_folder()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SaveAsync(Png, "students", "../evil.png"));
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SaveAsync(Png, "../escape", "a.png"));
        Assert.False(File.Exists(Path.Combine(_root, "evil.png")));
    }

    [Fact]
    public async Task Saved_file_is_stored_under_the_root_and_resolves_by_its_stored_path()
    {
        var stored = await _storage.SaveAsync(Png, FileStorage.StudentsFolder, "abc_photo.png");

        Assert.Equal("students/abc_photo.png", stored);
        Assert.True(File.Exists(Path.Combine(_root, "students", "abc_photo.png")));
        Assert.Equal(Png, await _storage.ReadAllBytesAsync(stored));
        Assert.Equal(Png, await _storage.ReadAllBytesAsync("/" + stored)); // a leading slash is tolerated
        Assert.Null(await _storage.ReadAllBytesAsync("students/missing.png"));
    }

    [Fact]
    public void Legacy_wwwroot_files_still_resolve_and_a_copy_in_the_new_root_is_preferred()
    {
        Directory.CreateDirectory(Path.Combine(_wwwroot, "uploads"));
        Directory.CreateDirectory(Path.Combine(_wwwroot, "Clg_detail", "logos"));
        File.WriteAllBytes(Path.Combine(_wwwroot, "uploads", "24240008_photo.png"), Png);
        // Written by Update (the typo folder) but recorded with either spelling.
        File.WriteAllBytes(Path.Combine(_wwwroot, "Clg_detail", "logos", "x.png"), Png);

        Assert.NotNull(_storage.ResolveExisting("/uploads/24240008_photo.png"));
        Assert.NotNull(_storage.ResolveExisting("/Clg_details/logos/x.png"));
        Assert.NotNull(_storage.ResolveExisting("/Clg_detail/logos/x.png"));

        // After the one-time ops copy into the new root, the new location wins.
        Directory.CreateDirectory(Path.Combine(_root, "students"));
        var copied = Path.Combine(_root, "students", "24240008_photo.png");
        File.WriteAllBytes(copied, Png);
        Assert.Equal(copied, _storage.ResolveExisting("/uploads/24240008_photo.png"));
    }

    [Fact]
    public void Only_images_have_a_content_type()
    {
        Assert.Equal("image/png", FileStorage.ImageContentType("a.png"));
        Assert.Equal("image/jpeg", FileStorage.ImageContentType("a.JPG"));
        Assert.Null(FileStorage.ImageContentType("a.html"));
        Assert.Null(FileStorage.ImageContentType("a.config"));
    }

    // ---- GET /api/Files: tenant scoping ----

    private static College NewCollege(Guid id, string? logo = null) => new()
    {
        CollegeId = id, Name = "C" + id.ToString("N")[..6], CollegeCode = "C1", CollegeCenter = "Main",
        ContactEmail = "a@b.c", ContactPhone = "1", LogoUrl = logo
    };

    [Fact]
    public async Task Files_endpoint_serves_own_college_files_and_hides_other_colleges_and_unreferenced_paths()
    {
        var own = Guid.NewGuid();
        var other = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(own);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        var controller = new FilesController(context, _storage)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var ownPhoto = await _storage.SaveAsync(Png, FileStorage.StudentsFolder, "own_photo.png");
        var otherPhoto = await _storage.SaveAsync(Png, FileStorage.StudentsFolder, "other_photo.png");
        var ownLogo = await _storage.SaveAsync(Png, FileStorage.CollegeLogosFolder, "own_logo.png");
        var orphan = await _storage.SaveAsync(Png, FileStorage.StudentsFolder, "orphan.png");

        context.Colleges.AddRange(NewCollege(own, ownLogo), NewCollege(other));
        context.StudentMasters.Add(new StudentMaster
        {
            StdMstId = Guid.NewGuid(), StudentId = "S1", FirstName = "A", LastName = "B", CollegeId = own, PhotoUrl = "/" + ownPhoto
        });
        context.StudentMasters.Add(new StudentMaster
        {
            StdMstId = Guid.NewGuid(), StudentId = "S2", FirstName = "C", LastName = "D", CollegeId = other, PhotoUrl = otherPhoto
        });
        await context.SaveChangesAsync();

        var ok = Assert.IsType<PhysicalFileResult>(await controller.Get(ownPhoto, default));
        Assert.Equal("image/png", ok.ContentType);
        Assert.IsType<PhysicalFileResult>(await controller.Get(ownLogo, default));

        Assert.IsType<NotFoundResult>(await controller.Get(otherPhoto, default));   // another college's student
        Assert.IsType<NotFoundResult>(await controller.Get(orphan, default));       // not referenced by any row
        Assert.IsType<BadRequestObjectResult>(await controller.Get("../own_photo.png", default));
        Assert.IsType<BadRequestObjectResult>(await controller.Get(null, default));
    }
}
