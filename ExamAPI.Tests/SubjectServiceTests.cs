using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Subject;
using ExamAPI.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace ExamAPI.Tests;

/// <summary>
/// Subject Master credits: what staff save must come back for them. The tenant filter hides any row
/// whose CollegeId is null, so every save (sync or async) has to stamp CollegeId on new rows.
/// </summary>
public sealed class SubjectServiceTests
{
    private readonly ApplicationDbContext _context;
    private readonly SubjectService _service;
    private readonly Guid _college = Guid.NewGuid();
    private readonly string _ay = Guid.NewGuid().ToString();

    public SubjectServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.CollegeId).Returns(_college);
        _context = new ApplicationDbContext(options, new Mock<IHttpContextAccessor>().Object, currentUser.Object);
        _service = new SubjectService(_context, new GenericRepository(_context));
    }

    private async Task<Guid> AddSubject()
    {
        var created = await _service.CreateSubjectAsync(new CreateSubjectDto
        {
            SubjectName = "Pharmaceutics (Theory)",
            SubjectCode = "PH101",
            SemId = "Sem-1",
            SemName = "Sem-1",
            Pattern = "NEP",
            CourseID = Guid.NewGuid(),
        });
        Assert.True(created.Success);
        return _context.SubjectMasters.Single().SubjectId;
    }

    private SaveCreditsDto OneCredit(Guid subjectId, string examOutOf = "60") => new()
    {
        SubjectId = subjectId,
        Ayid = _ay,
        Credits = new List<CreditDto>
        {
            new()
            {
                CreditNo = "2",
                ExamType = new List<string> { "ESE" },
                InternalType = new List<string> { "IA" },
                ExamOutOf = examOutOf,
                ExamPassing = "24",
                InternalOutOf = "40",
                InternalPassing = "16",
                PassingStrategy = "HeadWise",
            },
        },
    };

    [Fact]
    public async Task SavedCredits_AreReadBackForTheSameCollege()
    {
        var subjectId = await AddSubject();

        var saved = await _service.SaveCreditAsync(OneCredit(subjectId));
        Assert.True(saved.Success);

        // The row must belong to the college, or the tenant filter hides it from the screen.
        Assert.Equal(_college, _context.SubjectCreditMasters.Single().CollegeId);

        var check = await _service.CheckCreditAsync(new GetCredits { SubjectId = subjectId, Ayid = _ay });
        Assert.True(check.Success);

        var credits = await _service.GetSubjectCreditAsync(new GetCredits { SubjectId = subjectId, Ayid = _ay });
        var credit = Assert.Single(credits);
        Assert.Equal("2", credit.CreditNo);
        Assert.Equal("60", credit.ExamOutOf);
        Assert.Equal(new[] { "IA" }, credit.InternalType);
    }

    [Fact]
    public async Task UpdatedCredits_AreReadBackWithTheNewValues()
    {
        var subjectId = await AddSubject();
        await _service.SaveCreditAsync(OneCredit(subjectId));
        var existing = (await _service.GetSubjectCreditAsync(new GetCredits { SubjectId = subjectId, Ayid = _ay })).Single();

        var edit = OneCredit(subjectId, examOutOf: "70");
        edit.Credits[0].CreditId = existing.CreditId;
        edit.Credits[0].InternalType = new List<string>();
        var updated = await _service.UpdateCreditAsync(edit);
        Assert.True(updated.Success);

        var credit = (await _service.GetSubjectCreditAsync(new GetCredits { SubjectId = subjectId, Ayid = _ay })).Single();
        Assert.Equal("70", credit.ExamOutOf);
        Assert.Empty(credit.InternalType);
    }

    [Fact]
    public async Task DeletedCredits_AreSoftDeletedAndNoLongerListed()
    {
        var subjectId = await AddSubject();
        await _service.SaveCreditAsync(OneCredit(subjectId));

        var deleted = await _service.DeleteCreditAsync(new DeleteCreditDto
        {
            SubjectId = subjectId,
            Ayid = _ay,
            Credits = new List<DeleteCreditsDto>(),
        });
        Assert.True(deleted.Success);

        Assert.Empty(await _service.GetSubjectCreditAsync(new GetCredits { SubjectId = subjectId, Ayid = _ay }));
        // Soft delete, not a hard delete: the row is still there for the audit trail.
        Assert.Single(_context.SubjectCreditMasters.IgnoreQueryFilters());
        Assert.True(_context.SubjectCreditMasters.IgnoreQueryFilters().Single().IsDeleted);
    }

    [Fact]
    public void The_synchronous_save_also_stamps_the_college()
    {
        _context.CourseMasters.Add(new CourseMaster { CourseId = Guid.NewGuid(), Name = "Civil", CourseCode = "CIVIL" });
        _context.SaveChanges();

        Assert.Equal(_college, _context.CourseMasters.Single().CollegeId);
    }
}
