using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ExamAPI.Tests;

/// <summary>Student access, step 1: the new tables are seeded, college-scoped and audited like the rest.</summary>
public sealed class StudentAccessSchemaTests
{
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly Guid _college = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    private ApplicationDbContext Context(Guid? college)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_db).Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(college);
        return new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
    }

    private static StudentMaster Student(string id) => new() { StdMstId = Guid.NewGuid(), StudentId = id, FirstName = "Ravi", LastName = "Patil" };

    [Fact]
    public void The_four_task_types_are_seeded_with_their_expiry_rule()
    {
        using var context = Context(_college);
        context.Database.EnsureCreated();

        var types = context.TaskTypes.OrderBy(t => t.Code).ToList();

        Assert.Equal(new[] { TaskTypes.AtktPayment, TaskTypes.HallTicket, TaskTypes.Marksheet, TaskTypes.StudentMaster }, types.Select(t => t.Code));
        Assert.Equal(TaskExpiryRules.OnPayment, types.Single(t => t.Code == TaskTypes.AtktPayment).ExpiryRule);
        Assert.Equal(TaskExpiryRules.OnDeadline, types.Single(t => t.Code == TaskTypes.StudentMaster).ExpiryRule);
        Assert.All(types, t => Assert.True(t.RequiresOtp));
    }

    [Fact]
    public void A_new_student_is_active()
    {
        Assert.True(Student("2600001").IsActive);
    }

    [Fact]
    public async Task Task_links_and_emails_get_the_college_and_another_college_cannot_see_them()
    {
        var student = Student("2600001");
        using (var context = Context(_college))
        {
            context.StudentMasters.Add(student);
            context.TaskLinks.Add(new TaskLink { Id = Guid.NewGuid(), TaskTypeCode = TaskTypes.StudentMaster, StdMstId = student.StdMstId, ExpiresAt = DateTime.UtcNow.AddDays(7) });
            context.EmailQueue.Add(new EmailQueue { Id = Guid.NewGuid(), Kind = EmailKinds.TaskLink, ToAddress = "ravi@mail.com" });
            await context.SaveChangesAsync();
        }

        using (var mine = Context(_college))
        {
            Assert.Equal(_college, (await mine.TaskLinks.SingleAsync()).CollegeId);
            Assert.Equal(_college, (await mine.EmailQueue.SingleAsync()).CollegeId);
        }
        using (var theirs = Context(_other))
        {
            Assert.Empty(await theirs.TaskLinks.ToListAsync());
            Assert.Empty(await theirs.EmailQueue.ToListAsync());
            Assert.Empty(await theirs.StudentMasters.ToListAsync());
        }
    }

    [Fact]
    public async Task Automatic_audit_entries_belong_to_the_college_so_its_staff_can_see_them()
    {
        using (var context = Context(_college))
        {
            context.StudentMasters.Add(Student("2600001"));
            await context.SaveChangesAsync();
        }

        using var mine = Context(_college);
        var entry = await mine.AuditLogs.SingleAsync(a => a.TableName == nameof(StudentMaster));
        Assert.Equal(_college, entry.CollegeId);
        Assert.Null(entry.Event);
    }
}
