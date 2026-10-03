using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.Auth;
using ExamAPI.Services.Files;
using ExamAPI.Services.UsersMaster;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Platform
{
    public interface IProvisionCollegeService
    {
        /// <summary>
        /// Creates a usable college in one DB transaction, or - when the college code is already
        /// registered - fills in only the missing parts and returns the existing college.
        /// Throws <see cref="ArgumentException"/> / <see cref="InvalidOperationException"/> with a
        /// user-facing message on bad input; nothing is written in that case.
        /// </summary>
        Task<ProvisionSummary> ProvisionAsync(ProvisionCollegeRequest request, CancellationToken ct = default);
    }

    /// <summary>
    /// C# port of <c>_archive/sql-scripts/ProvisionPharmacyCollege.sql</c>. A college is not usable
    /// until it has ALL of: College, an AcademicYear marked current, CourseMaster rows (branches),
    /// PatternMaster rows, a GradeMaster with GradeThreshold rows (result processing throws without
    /// one), a RuleSet with Rule/RuleCondition/RuleAction rows (ProcessResults fails with "No active
    /// rule set found" without one), the college's "Admin" role and an admin user.
    /// <para>
    /// The grade scale and the ordinance rule sets are CLONED from a template college (same board,
    /// same ordinances); cloning rather than sharing keeps each college free to diverge. PatternId
    /// and GradeMasterId on every cloned rule set are remapped to the new college's own rows, the
    /// pattern being matched by name.
    /// </para>
    /// <para>
    /// The platform admin has no CollegeId, so the tenant query filter hides every tenant row from
    /// it: every lookup here uses IgnoreQueryFilters() and scopes by CollegeId explicitly (and
    /// re-applies the soft-delete predicate by hand). Admin users go through
    /// <see cref="IUserMasterService"/> so the 2-admin cap and BCrypt hashing are not duplicated.
    /// </para>
    /// </summary>
    public class ProvisionCollegeService : IProvisionCollegeService
    {
        private const long MaxImageSize = 2 * 1024 * 1024;
        private static readonly string[] AllowedImageTypes = { "image/jpeg", "image/png", "image/webp" };

        private readonly ApplicationDbContext _context;
        private readonly IUserMasterService _users;
        private readonly IFileStorage _storage;

        public ProvisionCollegeService(ApplicationDbContext context, IUserMasterService users, IFileStorage storage)
        {
            _context = context;
            _users = users;
            _storage = storage;
        }

        // Validated, trimmed request.
        private sealed record Input(
            string Name, string Code, string Center, string? Address, string Email, string Phone,
            string AyFull, string? AyShort, bool AyCurrent,
            List<(string Name, string Code)> Branches, List<string> Patterns, Guid? TemplateId,
            List<AdminInput> Admins);

        public async Task<ProvisionSummary> ProvisionAsync(ProvisionCollegeRequest request, CancellationToken ct = default)
        {
            var input = Validate(request);
            ValidateImage(request.Logo, "Logo");
            ValidateImage(request.Banner, "Banner");

            // The template must exist, and must not be the college being provisioned.
            if (input.TemplateId is Guid templateId)
            {
                var template = await _context.Colleges.IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.CollegeId == templateId && !c.IsDeleted)
                    .Select(c => new { c.CollegeCode }).FirstOrDefaultAsync(ct);
                if (template == null)
                    throw new ArgumentException("Template college not found.");
                if (string.Equals(template.CollegeCode, input.Code, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The template college cannot be the college being created.");
            }

            var summary = new ProvisionSummary { TemplateCollegeId = input.TemplateId };
            var savedFiles = new List<string>();

            // The in-memory test provider has no transactions.
            var tx = _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync(ct) : null;
            try
            {
                var college = await EnsureCollegeAsync(input, request, summary, savedFiles, ct);
                summary.CollegeId = college.CollegeId;
                summary.Name = college.Name;
                summary.CollegeCode = college.CollegeCode;

                await EnsureAcademicYearAsync(college.CollegeId, input, summary, ct);
                await EnsureBranchesAsync(college.CollegeId, input, summary, ct);
                await EnsurePatternsAsync(college.CollegeId, input, summary, ct);

                if (input.TemplateId is Guid tid)
                {
                    var gradeMap = await CloneGradeScalesAsync(tid, college.CollegeId, summary, ct);
                    await CloneRuleSetsAsync(tid, college.CollegeId, gradeMap, summary, ct);
                }
                else
                {
                    summary.Warnings.Add("No template college: no grade scale or ordinance rule set was created, so results cannot be processed until they are added.");
                }

                var (role, roleCreated) = await PlatformValidation.EnsureAdminRoleAsync(_context, college.CollegeId, ct);
                Count(summary, "Admin role", roleCreated);

                await EnsureAdminsAsync(college.CollegeId, role.RoleId, input.Admins, summary, ct);

                if (tx != null) await tx.CommitAsync(ct);
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                foreach (var key in savedFiles) _storage.Delete(key);
                throw;
            }
            finally
            {
                if (tx != null) await tx.DisposeAsync();
            }

            return summary;
        }

        // ---------------------------------------------------------------- steps

        private async Task<College> EnsureCollegeAsync(Input input, ProvisionCollegeRequest request,
            ProvisionSummary summary, List<string> savedFiles, CancellationToken ct)
        {
            var codeLower = input.Code.ToLower();
            var college = await _context.Colleges.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => !c.IsDeleted && c.CollegeCode.ToLower() == codeLower, ct);

            summary.AlreadyExisted = college != null;
            if (college == null)
            {
                college = new College
                {
                    CollegeId = Guid.NewGuid(),
                    Name = input.Name,
                    CollegeCode = input.Code,
                    CollegeCenter = input.Center,
                    Address = input.Address,
                    ContactEmail = input.Email,
                    ContactPhone = input.Phone,
                };
                _context.Colleges.Add(college);
            }
            Count(summary, "College", !summary.AlreadyExisted);

            // Images only fill a missing slot: a re-run never overwrites a college's branding.
            await EnsureImageAsync(request.Logo, college.LogoUrl, FileStorage.CollegeLogosFolder,
                key => college.LogoUrl = key, "Logo", summary, savedFiles, ct);
            await EnsureImageAsync(request.Banner, college.LogoBannerUrl, FileStorage.CollegeBannersFolder,
                key => college.LogoBannerUrl = key, "Banner", summary, savedFiles, ct);

            await _context.SaveChangesAsync(ct);
            return college;
        }

        private async Task EnsureImageAsync(IFormFile? file, string? current, string folder, Action<string> assign,
            string label, ProvisionSummary summary, List<string> savedFiles, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return;
            if (!string.IsNullOrEmpty(current))
            {
                Count(summary, label, false);
                return;
            }

            var extension = Path.GetExtension(file.FileName);
            if (extension.Length == 0 || extension.Length > 6 || !extension.Skip(1).All(char.IsLetterOrDigit))
                extension = ".png";

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var key = await _storage.SaveAsync(buffer.ToArray(), folder, $"{Guid.NewGuid()}{extension.ToLowerInvariant()}", ct);
            savedFiles.Add(key);
            assign(key);
            Count(summary, label, true);
        }

        private async Task EnsureAcademicYearAsync(Guid collegeId, Input input, ProvisionSummary summary, CancellationToken ct)
        {
            var years = await _context.AcademicYears.IgnoreQueryFilters()
                .Where(a => !a.IsDeleted && a.CollegeId == collegeId).ToListAsync(ct);

            var year = years.FirstOrDefault(a => string.Equals(a.FullDuration, input.AyFull, StringComparison.OrdinalIgnoreCase));
            var created = year == null;
            if (year == null)
            {
                year = new AcademicYear
                {
                    AYID = Guid.NewGuid(),
                    FullDuration = input.AyFull,
                    ShortDuration = input.AyShort,
                    CollegeId = collegeId,
                };
                _context.AcademicYears.Add(year);
                years.Add(year);
            }

            // The client reads the current year from its header; at most one year is ever current,
            // so a re-run never steals "current" from a year the college is already using.
            if (input.AyCurrent && !years.Any(a => a.IsCurrent))
                year.IsCurrent = true;

            Count(summary, "Academic year", created);
            await _context.SaveChangesAsync(ct);
        }

        private async Task EnsureBranchesAsync(Guid collegeId, Input input, ProvisionSummary summary, CancellationToken ct)
        {
            var existing = await _context.CourseMasters.IgnoreQueryFilters()
                .Where(c => !c.IsDeleted && c.CollegeId == collegeId)
                .Select(c => c.CourseCode).ToListAsync(ct);
            var codes = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

            foreach (var (name, code) in input.Branches)
            {
                var isNew = codes.Add(code);
                if (isNew)
                    _context.CourseMasters.Add(new CourseMaster
                    {
                        CourseId = Guid.NewGuid(), Name = name, CourseCode = code, CollegeId = collegeId,
                    });
                Count(summary, "Branches", isNew);
            }
            await _context.SaveChangesAsync(ct);
        }

        private async Task EnsurePatternsAsync(Guid collegeId, Input input, ProvisionSummary summary, CancellationToken ct)
        {
            var existing = await _context.PatternMasters.IgnoreQueryFilters()
                .Where(p => !p.IsDeleted && p.CollegeId == collegeId)
                .Select(p => p.PatternName).ToListAsync(ct);
            var names = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

            foreach (var name in input.Patterns)
            {
                var isNew = names.Add(name);
                if (isNew)
                    _context.PatternMasters.Add(new PatternMaster
                    {
                        PatternId = Guid.NewGuid(), PatternName = name, CollegeId = collegeId,
                    });
                Count(summary, "Patterns", isNew);
            }
            await _context.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Clones the template's grade scales WITH their thresholds (GradeThreshold has no CollegeId
        /// of its own; it inherits isolation through its GradeMaster). A scale the new college already
        /// has (same name) is reused. Returns template GradeMasterId -> the new college's GradeMasterId.
        /// </summary>
        private async Task<Dictionary<Guid, Guid>> CloneGradeScalesAsync(Guid templateId, Guid collegeId,
            ProvisionSummary summary, CancellationToken ct)
        {
            var source = await _context.GradeMasters.IgnoreQueryFilters().AsNoTracking()
                .Include(g => g.Thresholds)
                .Where(g => !g.IsDeleted && g.CollegeId == templateId)
                .OrderBy(g => g.CreatedAt)
                .ToListAsync(ct);

            var mine = await _context.GradeMasters.IgnoreQueryFilters()
                .Where(g => !g.IsDeleted && g.CollegeId == collegeId).ToListAsync(ct);

            var map = new Dictionary<Guid, Guid>();
            foreach (var scale in source)
            {
                var existing = mine.FirstOrDefault(g => string.Equals(g.Name, scale.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    map[scale.GradeMasterId] = existing.GradeMasterId;
                    Count(summary, "Grade scales", false);
                    continue;
                }

                var clone = new GradeMaster
                {
                    GradeMasterId = Guid.NewGuid(),
                    Name = scale.Name,
                    Description = scale.Description,
                    CollegeId = collegeId,
                };
                _context.GradeMasters.Add(clone);
                mine.Add(clone);
                map[scale.GradeMasterId] = clone.GradeMasterId;
                Count(summary, "Grade scales", true);

                foreach (var t in (scale.Thresholds ?? new List<GradeThreshold>()).Where(t => !t.IsDeleted))
                {
                    _context.GradeThresholds.Add(new GradeThreshold
                    {
                        ThresholdId = Guid.NewGuid(),
                        Grade = t.Grade,
                        GradePoint = t.GradePoint,
                        MinPercentage = t.MinPercentage,
                        MaxPercentage = t.MaxPercentage,
                        PerformanceRemark = t.PerformanceRemark,
                        GradeMasterId = clone.GradeMasterId,
                    });
                    Count(summary, "Grade thresholds", true);
                }
            }

            if (source.Count == 0)
                summary.Warnings.Add("The template college has no grade scale; none was created.");

            await _context.SaveChangesAsync(ct);
            return map;
        }

        /// <summary>
        /// Clones the template's ordinance rule sets with rules, conditions and actions. Rule sets
        /// without any rule are skipped (the engineering college carries an empty leftover set that
        /// would otherwise produce a college whose result processing silently does nothing). A rule
        /// set whose pattern name does not exist in the new college is skipped with a warning.
        /// </summary>
        private async Task CloneRuleSetsAsync(Guid templateId, Guid collegeId, Dictionary<Guid, Guid> gradeMap,
            ProvisionSummary summary, CancellationToken ct)
        {
            var source = await _context.RuleSets.IgnoreQueryFilters().AsNoTracking()
                .Include(r => r.Pattern)
                .Include(r => r.Rules!).ThenInclude(r => r.Conditions)
                .Include(r => r.Rules!).ThenInclude(r => r.Actions)
                .AsSplitQuery()
                .Where(r => !r.IsDeleted && r.CollegeId == templateId)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(ct);

            var patterns = await _context.PatternMasters.IgnoreQueryFilters()
                .Where(p => !p.IsDeleted && p.CollegeId == collegeId).ToListAsync(ct);
            var mine = await _context.RuleSets.IgnoreQueryFilters()
                .Where(r => !r.IsDeleted && r.CollegeId == collegeId)
                .Select(r => new { r.Name, r.PatternId }).ToListAsync(ct);

            var cloned = 0;
            foreach (var rs in source)
            {
                var rules = (rs.Rules ?? new List<Rule>()).Where(r => !r.IsDeleted).ToList();
                if (rules.Count == 0)
                {
                    summary.Warnings.Add($"Rule set \"{rs.Name}\" has no rules in the template and was skipped.");
                    continue;
                }

                var patternName = rs.Pattern?.PatternName;
                var pattern = patterns.FirstOrDefault(p => string.Equals(p.PatternName, patternName, StringComparison.OrdinalIgnoreCase));
                if (pattern == null)
                {
                    summary.Warnings.Add($"Rule set \"{rs.Name}\" belongs to pattern \"{patternName}\", which this college does not have; it was skipped.");
                    continue;
                }

                if (mine.Any(m => m.PatternId == pattern.PatternId && string.Equals(m.Name, rs.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    Count(summary, "Rule sets", false);
                    continue;
                }

                Guid? gradeMasterId = null;
                if (rs.GradeMasterId is Guid g)
                {
                    if (gradeMap.TryGetValue(g, out var mapped)) gradeMasterId = mapped;
                    else summary.Warnings.Add($"Rule set \"{rs.Name}\" uses a grade scale that is not in the template and was left without one.");
                }

                var clone = new RuleSet
                {
                    RuleSetId = Guid.NewGuid(),
                    Name = rs.Name,
                    ExamType = rs.ExamType,
                    IsActive = rs.IsActive,
                    PatternId = pattern.PatternId,
                    GradeMasterId = gradeMasterId,
                    CollegeId = collegeId,
                };
                _context.RuleSets.Add(clone);
                Count(summary, "Rule sets", true);
                cloned++;

                foreach (var rule in rules)
                {
                    var newRule = new Rule
                    {
                        RuleId = Guid.NewGuid(),
                        Name = rule.Name,
                        Priority = rule.Priority,
                        IsEnabled = rule.IsEnabled,
                        StopOnSuccess = rule.StopOnSuccess,
                        OrdinanceSymbol = rule.OrdinanceSymbol,
                        RuleSetId = clone.RuleSetId,
                    };
                    _context.Rules.Add(newRule);
                    Count(summary, "Rules", true);

                    foreach (var c in (rule.Conditions ?? new List<RuleCondition>()).Where(c => !c.IsDeleted))
                    {
                        _context.RuleConditions.Add(new RuleCondition
                        {
                            ConditionId = Guid.NewGuid(),
                            FactName = c.FactName,
                            Operator = c.Operator,
                            Value = c.Value,
                            RuleId = newRule.RuleId,
                        });
                        Count(summary, "Rule conditions", true);
                    }

                    foreach (var a in (rule.Actions ?? new List<RuleAction>()).Where(a => !a.IsDeleted))
                    {
                        _context.RuleActions.Add(new RuleAction
                        {
                            ActionId = Guid.NewGuid(),
                            ActionType = a.ActionType,
                            CalculationMode = a.CalculationMode,
                            Param1Type = a.Param1Type,
                            Param1Value = a.Param1Value,
                            Param2Type = a.Param2Type,
                            Param2Value = a.Param2Value,
                            MaxLimit = a.MaxLimit,
                            MaxTargetCount = a.MaxTargetCount,
                            Target = a.Target,
                            Expression = a.Expression,
                            RuleId = newRule.RuleId,
                        });
                        Count(summary, "Rule actions", true);
                    }
                }
            }

            if (cloned == 0 && !summary.Items.Any(i => i.Item == "Rule sets" && i.AlreadyPresent > 0))
                summary.Warnings.Add("No ordinance rule set was cloned; result processing will fail until one exists.");

            await _context.SaveChangesAsync(ct);
        }

        private async Task EnsureAdminsAsync(Guid collegeId, Guid roleId, List<AdminInput> admins,
            ProvisionSummary summary, CancellationToken ct)
        {
            foreach (var admin in admins)
            {
                var dto = PlatformValidation.Admin(admin, roleId, collegeId);

                // Re-run: an admin already registered in THIS college is left exactly as it is.
                var emailLower = dto.Email.ToLower();
                var existing = await _context.UserMasters.IgnoreQueryFilters().AsNoTracking()
                    .Where(u => !u.IsDeleted && u.Email.ToLower() == emailLower)
                    .Select(u => new { u.UserId, u.Username, u.Email, u.CollegeId })
                    .FirstOrDefaultAsync(ct);

                if (existing != null && existing.CollegeId == collegeId)
                {
                    summary.Admins.Add(new ProvisionAdminResult
                    {
                        UserId = existing.UserId, Username = existing.Username, Email = existing.Email, Status = "already present",
                    });
                    Count(summary, "Admins", false);
                    continue;
                }

                // Different college (or a platform login): CreateUserAsync rejects it with "Email already exists".
                // The 2-admin cap is enforced there too (platform caller), not re-implemented here.
                var created = await _users.CreateUserAsync(dto, collegeId, callerIsPlatformAdmin: true);
                summary.Admins.Add(new ProvisionAdminResult
                {
                    UserId = created.UserId, Username = created.Username, Email = created.Email, Status = "created",
                });
                Count(summary, "Admins", true);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static void Count(ProvisionSummary summary, string item, bool created)
        {
            var row = summary.Items.FirstOrDefault(i => i.Item == item);
            if (row == null)
            {
                row = new ProvisionItemCount { Item = item };
                summary.Items.Add(row);
            }
            if (created) row.Created++; else row.AlreadyPresent++;
        }

        private static void ValidateImage(IFormFile? file, string label)
        {
            if (file == null || file.Length == 0) return;
            if (!AllowedImageTypes.Contains(file.ContentType))
                throw new ArgumentException($"{label} format is not supported (use JPG, PNG or WEBP).");
            if (file.Length > MaxImageSize)
                throw new ArgumentException($"{label} must be less than 2MB.");
        }

        private static Input Validate(ProvisionCollegeRequest? r)
        {
            if (r == null) throw new ArgumentException("Request is required.");

            var name = PlatformValidation.Required(r.Name, "College name", 50);
            var code = PlatformValidation.Required(r.CollegeCode, "College code", 20);
            var center = PlatformValidation.Required(r.CollegeCenter, "College centre", 100);
            var address = PlatformValidation.Optional(r.Address, "Address", 500);
            var email = PlatformValidation.Email(r.ContactEmail, "Contact email");
            var phone = PlatformValidation.Required(r.ContactPhone, "Contact phone", 20);

            if (r.AcademicYear == null)
                throw new ArgumentException("Academic year is required.");
            var ayFull = PlatformValidation.Required(r.AcademicYear.FullDuration, "Academic year (full duration)", 50);
            var ayShort = PlatformValidation.ShortYear(r.AcademicYear.ShortDuration, ayFull);

            var branches = new List<(string, string)>();
            foreach (var b in r.Branches ?? new List<BranchInput>())
            {
                // An empty trailing row from the form is not an error.
                if (string.IsNullOrWhiteSpace(b?.Name) && string.IsNullOrWhiteSpace(b?.Code)) continue;
                var (bn, bc) = PlatformValidation.Branch(b);
                if (branches.Any(x => string.Equals(x.Item2, bc, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Branch code \"{bc}\" is listed twice.");
                branches.Add((bn, bc));
            }
            if (branches.Count == 0)
                throw new ArgumentException("At least one branch is required.");

            var patterns = new List<string>();
            foreach (var p in r.Patterns ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var pn = PlatformValidation.Required(p, "Pattern name", 100);
                if (patterns.Any(x => string.Equals(x, pn, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Pattern \"{pn}\" is listed twice.");
                patterns.Add(pn);
            }
            if (patterns.Count == 0)
                throw new ArgumentException("At least one pattern (e.g. NEP) is required.");

            var admins = (r.Admins ?? new List<AdminInput>())
                .Where(a => a != null && !(string.IsNullOrWhiteSpace(a.Username) && string.IsNullOrWhiteSpace(a.Email)
                                           && string.IsNullOrWhiteSpace(a.FirstName) && string.IsNullOrWhiteSpace(a.LastName)
                                           && string.IsNullOrEmpty(a.Password)))
                .ToList();
            if (admins.Count > AccessPolicies.MaxAdminsPerCollege)
                throw new ArgumentException($"A college can have at most {AccessPolicies.MaxAdminsPerCollege} admins.");
            // Fail on malformed admin details before anything is written.
            foreach (var a in admins) PlatformValidation.Admin(a, Guid.Empty, Guid.Empty);

            return new Input(name, code, center, address, email, phone, ayFull, ayShort, r.AcademicYear.IsCurrent,
                branches, patterns, r.TemplateCollegeId == Guid.Empty ? null : r.TemplateCollegeId, admins);
        }
    }
}
