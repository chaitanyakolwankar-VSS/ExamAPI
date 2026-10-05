using ExamAPI.Data;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Platform
{
    /// <summary>
    /// What an EMPTY production database needs before anyone can sign in (DEC-26): the platform
    /// admin and the screen catalog. Colleges, courses and users are then created on the Platform
    /// page; grade scales and rule sets come from the starter templates (deploy/SeedStarterTemplates.sql).
    /// <para>
    /// Runs on every start and only ever adds: the platform admin is created from
    /// <c>Bootstrap:PlatformAdminEmail</c> / <c>Bootstrap:PlatformAdminPassword</c> when no platform
    /// admin exists (remove the password from the settings after the first sign-in), and the screen
    /// catalog only when the Permission table has no live row (an existing catalog is never touched).
    /// </para>
    /// </summary>
    public static class StartupSeeder
    {
        /// <summary>
        /// The staff screens, as (module, form name). The names are the labels in the client's
        /// src/config/screens.ts, which a Permission row is matched against. Admin-only screens
        /// (Role Master, Create User, ...) are not permissions and are not listed.
        /// </summary>
        public static readonly (string Module, string Form)[] Screens =
        {
            ("Academic Master", "Exam Master"),
            ("Academic Master", "Ordinances"),
            ("Academic Master", "Student Promotion"),
            ("Academic Master", "Subject Master"),
            ("Students Admin", "Declare Result"),
            ("Students Admin", "Release Hallticket"),
            ("Students Admin", "Student Master"),
            ("Conduct Exam", "Regular Exam"),
            ("Conduct Exam", "ATKT/Reval Exam"),
            ("Conduct Exam", "Assign Seat No"),
            ("Marks Entry", "Enter Marks"),
            ("Marks Entry", "Apply Grace Marks"),
            ("Marks Entry", "Enter Eligibility"),
            ("Reports", "Generate Hallticket"),
            ("Reports", "Generate Gazette"),
            ("Reports", "Generate Result"),
            ("Reports", "Student Assign Report"),
            ("Reports", "ATKT Cummulative Report"),
            ("Reports", "Statistic Report"),
        };

        public const int MinPasswordLength = 8;

        public static async Task SeedAsync(ApplicationDbContext context, IConfiguration config, ILogger logger, CancellationToken ct = default)
        {
            await SeedScreensAsync(context, logger, ct);
            await SeedPlatformAdminAsync(context, config, logger, ct);
        }

        public static async Task SeedScreensAsync(ApplicationDbContext context, ILogger logger, CancellationToken ct = default)
        {
            if (await context.Permissions.IgnoreQueryFilters().AnyAsync(p => !p.IsDeleted, ct)) return;

            foreach (var (module, form) in Screens)
            {
                context.Permissions.Add(new Permission
                {
                    PermissionId = Guid.NewGuid(),
                    PermissionModuleName = module,
                    PermissionFormName = form,
                });
            }
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Startup: added the screen catalog ({Count} screens).", Screens.Length);
        }

        public static async Task SeedPlatformAdminAsync(ApplicationDbContext context, IConfiguration config, ILogger logger, CancellationToken ct = default)
        {
            var email = config["Bootstrap:PlatformAdminEmail"]?.Trim();
            var password = config["Bootstrap:PlatformAdminPassword"];

            var exists = await context.UserMasters.IgnoreQueryFilters().AnyAsync(u => u.IsPlatformAdmin && !u.IsDeleted, ct);
            if (exists)
            {
                if (!string.IsNullOrEmpty(password))
                    logger.LogWarning("Startup: a platform admin exists; remove Bootstrap:PlatformAdminPassword from the settings.");
                return;
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            {
                logger.LogWarning("Startup: there is no platform admin. Set Bootstrap:PlatformAdminEmail and Bootstrap:PlatformAdminPassword and restart to create one.");
                return;
            }
            if (password.Length < MinPasswordLength)
            {
                logger.LogError("Startup: Bootstrap:PlatformAdminPassword must be at least {Min} characters; no platform admin was created.", MinPasswordLength);
                return;
            }
            if (await context.UserMasters.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            {
                logger.LogError("Startup: {Email} already belongs to a college user; no platform admin was created.", email);
                return;
            }

            context.UserMasters.Add(new UserMaster
            {
                UserId = Guid.NewGuid(),
                Username = "platform.admin",
                Email = email,
                HashedPassword = BCrypt.Net.BCrypt.HashPassword(password),
                FirstName = "Platform",
                LastName = "Admin",
                IsPlatformAdmin = true,
                CollegeId = null,
                RoleId = null,
            });
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Startup: created the platform admin {Email}.", email);
        }
    }
}
