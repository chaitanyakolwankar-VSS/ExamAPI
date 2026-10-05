using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Auth;
using ExamAPI.Services.UsersMaster;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Platform
{
    public interface IPlatformCollegeService
    {
        Task<List<PlatformCollegeListItem>> ListAsync(CancellationToken ct = default);

        /// <summary>Null when the college does not exist.</summary>
        Task<PlatformCollegeDetail?> GetAsync(Guid collegeId, CancellationToken ct = default);

        Task<PlatformAdminDto> AddAdminAsync(Guid collegeId, AdminInput input, CancellationToken ct = default);

        /// <summary>Soft-deletes one of the college's admins. False when no such admin exists in that college.</summary>
        Task<bool> RemoveAdminAsync(Guid collegeId, Guid userId, CancellationToken ct = default);

        Task<PlatformBranchDto> AddBranchAsync(Guid collegeId, BranchInput input, CancellationToken ct = default);

        Task<PlatformAcademicYearDto> AddAcademicYearAsync(Guid collegeId, AddAcademicYearRequest input, CancellationToken ct = default);

        /// <summary>Makes an existing year the current one and unsets the previous current year.</summary>
        Task<PlatformAcademicYearDto> SetCurrentAcademicYearAsync(Guid collegeId, Guid ayId, CancellationToken ct = default);
    }

    /// <summary>
    /// Platform-admin operations on colleges other than first-time provisioning. The platform admin
    /// has no CollegeId, so the tenant query filter hides tenant rows from it; every query here uses
    /// IgnoreQueryFilters(), re-applies the soft-delete predicate and scopes by CollegeId explicitly.
    /// Throws <see cref="KeyNotFoundException"/> (college/year not found), <see cref="ArgumentException"/>
    /// (bad input) or <see cref="InvalidOperationException"/> (rule violation, e.g. the 2-admin cap),
    /// each with a user-facing message.
    /// </summary>
    public class PlatformCollegeService : IPlatformCollegeService
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserMasterService _users;

        public PlatformCollegeService(ApplicationDbContext context, IUserMasterService users)
        {
            _context = context;
            _users = users;
        }

        private static readonly string AdminRoleLower = AccessPolicies.AdminRoleName.ToLower();

        public async Task<List<PlatformCollegeListItem>> ListAsync(CancellationToken ct = default)
        {
            var colleges = await _context.Colleges.IgnoreQueryFilters().AsNoTracking()
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.Name)
                .Select(c => new PlatformCollegeListItem
                {
                    CollegeId = c.CollegeId,
                    Name = c.Name,
                    CollegeCode = c.CollegeCode,
                    HasLogo = c.LogoUrl != null && c.LogoUrl != "",
                    HasBanner = c.LogoBannerUrl != null && c.LogoBannerUrl != "",
                    IsTemplate = c.IsTemplate,
                })
                .ToListAsync(ct);

            var adminCounts = await AdminsQuery()
                .GroupBy(u => u.CollegeId)
                .Select(g => new { CollegeId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var branchCounts = await _context.CourseMasters.IgnoreQueryFilters().AsNoTracking()
                .Where(c => !c.IsDeleted)
                .GroupBy(c => c.CollegeId)
                .Select(g => new { CollegeId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var currentYears = await _context.AcademicYears.IgnoreQueryFilters().AsNoTracking()
                .Where(a => !a.IsDeleted && a.IsCurrent)
                .Select(a => new { a.CollegeId, a.ShortDuration, a.FullDuration })
                .ToListAsync(ct);

            foreach (var c in colleges)
            {
                c.AdminCount = adminCounts.FirstOrDefault(x => x.CollegeId == c.CollegeId)?.Count ?? 0;
                c.BranchCount = branchCounts.FirstOrDefault(x => x.CollegeId == c.CollegeId)?.Count ?? 0;
                var ay = currentYears.FirstOrDefault(x => x.CollegeId == c.CollegeId);
                c.CurrentAcademicYear = ay == null ? null : (string.IsNullOrWhiteSpace(ay.ShortDuration) ? ay.FullDuration : ay.ShortDuration);
            }
            return colleges;
        }

        public async Task<PlatformCollegeDetail?> GetAsync(Guid collegeId, CancellationToken ct = default)
        {
            var college = await _context.Colleges.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.CollegeId == collegeId && !c.IsDeleted)
                .FirstOrDefaultAsync(ct);
            if (college == null) return null;

            var detail = new PlatformCollegeDetail
            {
                CollegeId = college.CollegeId,
                Name = college.Name,
                CollegeCode = college.CollegeCode,
                CollegeCenter = college.CollegeCenter,
                Address = college.Address,
                ContactEmail = college.ContactEmail,
                ContactPhone = college.ContactPhone,
                HasLogo = !string.IsNullOrEmpty(college.LogoUrl),
                HasBanner = !string.IsNullOrEmpty(college.LogoBannerUrl),
            };

            detail.Branches = await _context.CourseMasters.IgnoreQueryFilters().AsNoTracking()
                .Where(c => !c.IsDeleted && c.CollegeId == collegeId)
                .OrderBy(c => c.Name)
                .Select(c => new PlatformBranchDto { CourseId = c.CourseId, Name = c.Name, CourseCode = c.CourseCode })
                .ToListAsync(ct);

            detail.Patterns = await _context.PatternMasters.IgnoreQueryFilters().AsNoTracking()
                .Where(p => !p.IsDeleted && p.CollegeId == collegeId)
                .OrderBy(p => p.PatternName)
                .Select(p => new PlatformPatternDto { PatternId = p.PatternId, PatternName = p.PatternName })
                .ToListAsync(ct);

            detail.AcademicYears = await _context.AcademicYears.IgnoreQueryFilters().AsNoTracking()
                .Where(a => !a.IsDeleted && a.CollegeId == collegeId)
                .OrderByDescending(a => a.FullDuration)
                .Select(a => new PlatformAcademicYearDto
                {
                    AYID = a.AYID, FullDuration = a.FullDuration, ShortDuration = a.ShortDuration, IsCurrent = a.IsCurrent,
                })
                .ToListAsync(ct);

            detail.Admins = await AdminsQuery().Where(u => u.CollegeId == collegeId)
                .OrderBy(u => u.CreatedAt)
                .Select(u => new PlatformAdminDto
                {
                    UserId = u.UserId, Username = u.Username, Email = u.Email, FirstName = u.FirstName, LastName = u.LastName,
                })
                .ToListAsync(ct);

            detail.GradeScaleCount = await _context.GradeMasters.IgnoreQueryFilters()
                .CountAsync(g => !g.IsDeleted && g.CollegeId == collegeId, ct);
            detail.RuleSetCount = await _context.RuleSets.IgnoreQueryFilters()
                .CountAsync(r => !r.IsDeleted && r.CollegeId == collegeId, ct);

            return detail;
        }

        public async Task<PlatformAdminDto> AddAdminAsync(Guid collegeId, AdminInput input, CancellationToken ct = default)
        {
            await RequireCollegeAsync(collegeId, ct);
            if (await _context.Colleges.IgnoreQueryFilters().AnyAsync(c => c.CollegeId == collegeId && c.IsTemplate, ct))
                throw new ArgumentException("A starter template is not a college and cannot have admins.");
            var (role, _) = await PlatformValidation.EnsureAdminRoleAsync(_context, collegeId, ct);
            var dto = PlatformValidation.Admin(input, role.RoleId, collegeId);

            // Username/email uniqueness and the 2-admin cap are enforced by the user service.
            var created = await _users.CreateUserAsync(dto, collegeId, callerIsPlatformAdmin: true);
            return new PlatformAdminDto
            {
                UserId = created.UserId, Username = created.Username, Email = created.Email,
                FirstName = created.FirstName, LastName = created.LastName,
            };
        }

        public async Task<bool> RemoveAdminAsync(Guid collegeId, Guid userId, CancellationToken ct = default)
        {
            await RequireCollegeAsync(collegeId, ct);

            // Only an admin OF THIS college: the platform admin can reach any user by id, so the
            // college and the role are checked here rather than trusting the route.
            var isAdminOfCollege = await AdminsQuery().AnyAsync(u => u.UserId == userId && u.CollegeId == collegeId, ct);
            if (!isAdminOfCollege) return false;

            return await _users.DeleteUserById(userId, callerIsPlatformAdmin: true);
        }

        public async Task<PlatformBranchDto> AddBranchAsync(Guid collegeId, BranchInput input, CancellationToken ct = default)
        {
            await RequireCollegeAsync(collegeId, ct);
            var (name, code) = PlatformValidation.Branch(input);

            var codeLower = code.ToLower();
            if (await _context.CourseMasters.IgnoreQueryFilters()
                    .AnyAsync(c => !c.IsDeleted && c.CollegeId == collegeId && c.CourseCode.ToLower() == codeLower, ct))
                throw new InvalidOperationException($"Branch code \"{code}\" already exists in this college.");

            var course = new CourseMaster { CourseId = Guid.NewGuid(), Name = name, CourseCode = code, CollegeId = collegeId };
            _context.CourseMasters.Add(course);
            await _context.SaveChangesAsync(ct);
            return new PlatformBranchDto { CourseId = course.CourseId, Name = course.Name, CourseCode = course.CourseCode };
        }

        public async Task<PlatformAcademicYearDto> AddAcademicYearAsync(Guid collegeId, AddAcademicYearRequest input, CancellationToken ct = default)
        {
            await RequireCollegeAsync(collegeId, ct);
            if (input == null) throw new ArgumentException("Academic year is required.");
            var full = PlatformValidation.Required(input.FullDuration, "Academic year (full duration)", 50);
            var shortName = PlatformValidation.ShortYear(input.ShortDuration, full);

            var years = await _context.AcademicYears.IgnoreQueryFilters()
                .Where(a => !a.IsDeleted && a.CollegeId == collegeId).ToListAsync(ct);
            if (years.Any(a => string.Equals(a.FullDuration, full, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Academic year \"{full}\" already exists in this college.");

            var year = new AcademicYear
            {
                AYID = Guid.NewGuid(), FullDuration = full, ShortDuration = shortName, CollegeId = collegeId,
            };

            // A college must have a current year (the client reads it from its header), so the first
            // year added becomes current whatever was asked. Otherwise at most one year is current:
            // the previous one is unset in the same save.
            var makeCurrent = input.SetCurrent || !years.Any(a => a.IsCurrent);
            if (makeCurrent)
            {
                foreach (var y in years.Where(a => a.IsCurrent)) y.IsCurrent = false;
                year.IsCurrent = true;
            }

            _context.AcademicYears.Add(year);
            await _context.SaveChangesAsync(ct);
            return ToDto(year);
        }

        public async Task<PlatformAcademicYearDto> SetCurrentAcademicYearAsync(Guid collegeId, Guid ayId, CancellationToken ct = default)
        {
            await RequireCollegeAsync(collegeId, ct);
            var years = await _context.AcademicYears.IgnoreQueryFilters()
                .Where(a => !a.IsDeleted && a.CollegeId == collegeId).ToListAsync(ct);
            var target = years.FirstOrDefault(a => a.AYID == ayId)
                ?? throw new KeyNotFoundException("Academic year not found in this college.");

            foreach (var y in years.Where(a => a.IsCurrent && a.AYID != ayId)) y.IsCurrent = false;
            target.IsCurrent = true;
            await _context.SaveChangesAsync(ct);
            return ToDto(target);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Live users of the "Admin" role (any college), tenant filter bypassed.</summary>
        private IQueryable<UserMaster> AdminsQuery() =>
            _context.UserMasters.IgnoreQueryFilters().AsNoTracking()
                .Where(u => !u.IsDeleted && u.Role != null && !u.Role.IsDeleted
                            && u.Role.Name.Trim().ToLower() == AdminRoleLower);

        private async Task RequireCollegeAsync(Guid collegeId, CancellationToken ct)
        {
            if (!await _context.Colleges.IgnoreQueryFilters().AnyAsync(c => c.CollegeId == collegeId && !c.IsDeleted, ct))
                throw new KeyNotFoundException("College not found.");
        }

        private static PlatformAcademicYearDto ToDto(AcademicYear a) => new()
        {
            AYID = a.AYID, FullDuration = a.FullDuration, ShortDuration = a.ShortDuration, IsCurrent = a.IsCurrent,
        };
    }
}
