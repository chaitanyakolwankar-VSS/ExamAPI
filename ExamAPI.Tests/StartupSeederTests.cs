using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Platform;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// T-44: an empty production database gets the platform admin (from Bootstrap:* settings) and the
/// screen catalog on first start, and nothing else; later starts never change existing data.
/// </summary>
public sealed class StartupSeederTests
{
    private static ApplicationDbContext NewContext()
    {
        var cu = new Mock<ICurrentUser>();
        cu.SetupGet(u => u.IsPlatformAdmin).Returns(true);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, cu.Object);
    }

    private static IConfiguration Config(string? email, string? password) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bootstrap:PlatformAdminEmail"] = email,
            ["Bootstrap:PlatformAdminPassword"] = password,
        }).Build();

    [Fact]
    public async Task Empty_database_gets_the_platform_admin_and_the_screen_catalog()
    {
        using var ctx = NewContext();
        await StartupSeeder.SeedAsync(ctx, Config("owner@example.test", "Long-enough-1"), NullLogger.Instance);

        var admin = await ctx.UserMasters.IgnoreQueryFilters().SingleAsync();
        Assert.True(admin.IsPlatformAdmin);
        Assert.Null(admin.CollegeId);
        Assert.Equal("owner@example.test", admin.Email);
        Assert.True(BCrypt.Net.BCrypt.Verify("Long-enough-1", admin.HashedPassword));

        var screens = await ctx.Permissions.IgnoreQueryFilters().Select(p => p.PermissionFormName).ToListAsync();
        Assert.Equal(StartupSeeder.Screens.Length, screens.Count);
        Assert.Contains("Enter Marks", screens);
    }

    [Fact]
    public async Task A_second_start_changes_nothing_even_with_other_settings()
    {
        using var ctx = NewContext();
        await StartupSeeder.SeedAsync(ctx, Config("owner@example.test", "Long-enough-1"), NullLogger.Instance);
        var hash = (await ctx.UserMasters.IgnoreQueryFilters().SingleAsync()).HashedPassword;

        await StartupSeeder.SeedAsync(ctx, Config("other@example.test", "Another-pass-2"), NullLogger.Instance);

        var admin = await ctx.UserMasters.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("owner@example.test", admin.Email);
        Assert.Equal(hash, admin.HashedPassword);
        Assert.Equal(StartupSeeder.Screens.Length, await ctx.Permissions.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task An_existing_catalog_is_left_alone()
    {
        using var ctx = NewContext();
        ctx.Permissions.Add(new Permission { PermissionId = Guid.NewGuid(), PermissionModuleName = "Academic Master", PermissionFormName = "Exam Masters" });
        await ctx.SaveChangesAsync();

        await StartupSeeder.SeedAsync(ctx, Config(null, null), NullLogger.Instance);

        Assert.Equal("Exam Masters", (await ctx.Permissions.SingleAsync()).PermissionFormName);
    }

    [Theory]
    [InlineData(null, "Long-enough-1")]
    [InlineData("owner@example.test", null)]
    [InlineData("owner@example.test", "short")]
    public async Task Missing_or_weak_settings_create_no_admin(string? email, string? password)
    {
        using var ctx = NewContext();
        await StartupSeeder.SeedAsync(ctx, Config(email, password), NullLogger.Instance);
        Assert.False(await ctx.UserMasters.IgnoreQueryFilters().AnyAsync());
    }
}
