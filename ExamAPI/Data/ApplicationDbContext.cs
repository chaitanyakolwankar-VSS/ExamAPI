using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExamAPI.Data
{
    public class ApplicationDbContext : DbContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;


        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IHttpContextAccessor httpContextAccessor) : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        // =========================================
        // 1. Organization & Settings
        // =========================================
        public DbSet<College> Colleges { get; set; }
        public DbSet<AcademicYear> AcademicYears { get; set; }

        // =========================================
        // 2. Identity & Security
        // =========================================
        public DbSet<UserMaster> UserMasters { get; set; }
        public DbSet<RoleMaster> RoleMasters { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<UserPermission> UserPermissions { get; set; }

        // =========================================
        // 3. Course Management
        // =========================================
        public DbSet<CourseMaster> CourseMasters { get; set; }
        public DbSet<SubjectMaster> SubjectMasters { get; set; }
        public DbSet<SubjectCreditMaster> SubjectCreditMasters { get; set; }
        public DbSet<SubjectCredits> SubjectCredits { get; set; }

        // =========================================
        // 4. Student Details
        // =========================================
        public DbSet<StudentMaster> StudentMasters { get; set; }
        public DbSet<StudentEligibility> StudentEligibilities { get; set; }

        // =========================================
        // 5. Examinations & Results
        // =========================================
        public DbSet<ExamMaster> Exams { get; set; }
        public DbSet<ResolutionMaster> Resolution { get; set; }
        public DbSet<TimeTableMaster> TimeTables { get; set; }
        public DbSet<MarksMaster> MarksMasters { get; set; }
        public DbSet<StudentMarks> StudentMarks { get; set; }
        public DbSet<StudentsOverallResult> StudentsOverallResults { get; set; }

        // =========================================
        // 6. Rules & Grading Logic
        // =========================================
        public DbSet<PatternMaster> PatternMasters { get; set; }
        public DbSet<RuleSet> RuleSets { get; set; }
        public DbSet<Rule> Rules { get; set; }
        public DbSet<RuleCondition> RuleConditions { get; set; }
        public DbSet<RuleAction> RuleActions { get; set; }
        public DbSet<GraceLookup> GraceLookups { get; set; }
        public DbSet<GradeMaster> GradeMasters { get; set; }
        public DbSet<GradeThreshold> GradeThresholds { get; set; }

        // =========================================
        // 7. AuditLog
        // =========================================
        public DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ---------------------------------------------------------
            // Configuration for Composite Keys (Many-to-Many Junctions)
            // ---------------------------------------------------------

            // RolePermission: The primary key is a combination of RoleId + PermissionId
            modelBuilder.Entity<RolePermission>()
                .HasKey(rp => new { rp.RoleId, rp.PermissionId });

            modelBuilder.Entity<RolePermission>()
                .HasOne(rp => rp.Role)
                .WithMany(rm => rm.RolePermissions)
                .HasForeignKey(rp => rp.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RolePermission>()
                .HasOne(rp => rp.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(rp => rp.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);


            // UserPermission: The primary key is a combination of UserId + PermissionId
            modelBuilder.Entity<UserPermission>()
                .HasKey(up => new { up.UserId, up.PermissionId });

            modelBuilder.Entity<UserPermission>()
                .HasOne(up => up.User)
                .WithMany(u => u.UserPermissions)
                .HasForeignKey(up => up.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserPermission>()
                .HasOne(up => up.Permission)
                .WithMany(p => p.UserPermissions)
                .HasForeignKey(up => up.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);


            // ---------------------------------------------------------
            // Additional Configurations (Optional but Recommended)
            // ---------------------------------------------------------

            // Ensure Usernames are unique
            modelBuilder.Entity<UserMaster>()
                .HasIndex(u => u.Username)
                .IsUnique();

            // Ensure Email is unique 
            modelBuilder.Entity<UserMaster>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // Ensure StudentId is unique
            modelBuilder.Entity<StudentMaster>()
                .HasIndex(s => s.StudentId)
                .IsUnique();

            // Configure Decimal Precision for Grades 
            modelBuilder.Entity<RuleAction>()
                .Property(p => p.Param1Value)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<RuleAction>()
                .Property(p => p.Param2Value)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<RuleAction>()
                .Property(p => p.MaxLimit)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<GradeThreshold>()
                .Property(p => p.MinPercentage)
                .HasColumnType("decimal(5,2)");

            modelBuilder.Entity<GradeThreshold>()
                .Property(p => p.MaxPercentage)
                .HasColumnType("decimal(5,2)");

            // =========================================================
            // GLOBAL CONFIGURATION
            // =========================================================
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                // A. Force Singular Table Names (Your previous request)
                if (!entityType.IsOwned())
                {
                    entityType.SetTableName(entityType.ClrType.Name);
                }

                // B. Apply Filter if the class inherits from BaseEntity
                if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                {
                    //isdelsted set 0 
                    modelBuilder.Entity(entityType.ClrType).Property(nameof(BaseEntity.IsDeleted)).HasDefaultValue(false);

                    // This creates the logic: "Where(e => e.IsDeleted == false)"
                    var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                    var prop = System.Linq.Expressions.Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
                    var condition = System.Linq.Expressions.Expression.Equal(prop, System.Linq.Expressions.Expression.Constant(false));
                    var lambda = System.Linq.Expressions.Expression.Lambda(condition, parameter);

                    modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
                }
            }


        }

        // =========================================================
        // UNIVERSAL SAVE: Handles both "BaseEntity" setup AND "Audit Logging"
        // =========================================================
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var userIdString = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? currentUserId = userIdString != null ? Guid.Parse(userIdString) : null;

            var role = user?.FindFirst(ClaimTypes.Role)?.Value;
            var userType = (role == "Student") ? "Student" : "Staff";

            var entries = ChangeTracker.Entries()
                .Where(e => e.Entity is BaseEntity &&
                           (e.State == EntityState.Added ||
                            e.State == EntityState.Modified ||
                            e.State == EntityState.Deleted))
                .ToList();

            var auditEntries = new List<AuditLog>();

            foreach (var entry in entries)
            {
                var entity = (BaseEntity)entry.Entity;

                
                if (entry.State == EntityState.Added)
                {
                    // NEW RECORD
                    entity.CreatedAt = DateTime.UtcNow;
                    entity.CreatedBy = currentUserId ?? entity.CreatedBy;
                    entity.IsDeleted = false;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(BaseEntity.CreatedBy)).IsModified = false;

                    entity.UpdatedAt = DateTime.UtcNow;
                }

                // 4. AUDIT LOGGING LOGIC
                string actionType = entry.State.ToString().ToUpper();

                // Detect Soft Delete (IsDeleted changed to true)
                if (entry.State == EntityState.Modified)
                {
                    var isDeletedProp = entry.Property("IsDeleted");
                    if (isDeletedProp.IsModified && (bool)isDeletedProp.CurrentValue == true)
                    {
                        actionType = "DELETE";
                    }
                }

                // Get Record ID safely
                var propName = entity.GetType().Name + "Id";
                var idProp = entity.GetType().GetProperty(propName)
                             ?? entity.GetType().GetProperty("Id")
                             ?? entity.GetType().GetProperties().FirstOrDefault(p => p.Name.EndsWith("Id"));

                string recordId = idProp?.GetValue(entity)?.ToString() ?? "Unknown";

                // Create the Log
                auditEntries.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    Action = actionType,
                    TableName = entity.GetType().Name,
                    RecordId = recordId,
                    PerformedBy = currentUserId,
                    UserType = userType,
                    Timestamp = DateTime.UtcNow
                });
            }

            // SAVE EVERYTHING (Data + Logs)
            // -----------------------------------------------------
            if (auditEntries.Count > 0)
            {
                await AuditLogs.AddRangeAsync(auditEntries);
            }

                return await base.SaveChangesAsync(cancellationToken);
        }
    }
}