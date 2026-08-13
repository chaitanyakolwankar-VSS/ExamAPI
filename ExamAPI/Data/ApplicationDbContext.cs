using ExamAPI.Models;
using ExamAPI.Services.Tenancy;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Security.Claims;

namespace ExamAPI.Data
{
    public class ApplicationDbContext : DbContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICurrentUser? _currentUser;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IHttpContextAccessor httpContextAccessor) : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            IHttpContextAccessor httpContextAccessor,
            ICurrentUser currentUser) : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
            _currentUser = currentUser;
        }

        /// <summary>
        /// The tenant every query is filtered to. Read as a PROPERTY inside the global query
        /// filters so EF turns it into a query parameter evaluated per request -- if it were
        /// captured as a constant, the first caller's college would be baked into the
        /// compiled model and served to everyone else.
        /// </summary>
        public Guid? CurrentCollegeId => _currentUser?.CollegeId;

        // =========================================
        // 0. PasswordReset and OTP for User Master
        // =========================================
        public DbSet<PasswordResetOTP> PasswordResetOTPs { get; set; }

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
        public DbSet<StudentSubjectResult> StudentSubjectResults { get; set; }
        public DbSet<StudentsOverallResult> StudentsOverallResults { get; set; }
        public DbSet<ExamAssignmentPolicy> ExamAssignmentPolicies { get; set; }

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

            // ---------------------------------------------------------
            // Multi-tenant uniqueness.
            // Username and StudentId are unique WITHIN a college, not globally: every
            // college needs its own "admin", and two colleges may legitimately issue the
            // same roll number. Email stays globally unique because it is the login
            // identifier -- one lookup on Email must resolve to exactly one user, and that
            // user's CollegeId is what establishes the tenant for the whole session.
            // ---------------------------------------------------------
            modelBuilder.Entity<UserMaster>()
                .HasIndex(u => new { u.CollegeId, u.Username })
                .IsUnique();

            modelBuilder.Entity<UserMaster>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<StudentMaster>()
                .HasIndex(s => new { s.CollegeId, s.StudentId })
                .IsUnique();

            // One computed verdict per student-subject. StudentMarks is not re-parented --
            // the two children of MarksMaster align on the natural key (MarksId, SubjectId).
            modelBuilder.Entity<StudentSubjectResult>()
                .HasIndex(r => new { r.MarksId, r.SubjectId })
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

            // AcademicYear.CollegeId became nullable in the CLR so the type could implement
            // ICollegeScoped, but the column must stay NOT NULL -- an academic year with no
            // college is meaningless. IsRequired() keeps the schema unchanged.
            modelBuilder.Entity<AcademicYear>()
                .Property(a => a.CollegeId)
                .IsRequired();

            // Same treatment for exam-assignment policies: the CLR property is nullable to
            // satisfy ICollegeScoped, but a policy that belongs to no college would be visible
            // to every tenant, which is not a state this table is allowed to reach.
            modelBuilder.Entity<ExamAssignmentPolicy>()
                .Property(p => p.CollegeId)
                .IsRequired();

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

                // B. Global query filter: soft-delete AND tenant isolation.
                //
                // These MUST be built as a single combined lambda. EF Core allows only one
                // HasQueryFilter per entity -- calling it twice silently REPLACES the first,
                // which would quietly drop the soft-delete filter the whole app relies on.
                var isSoftDeletable = typeof(BaseEntity).IsAssignableFrom(entityType.ClrType);
                var isTenantScoped = typeof(ICollegeScoped).IsAssignableFrom(entityType.ClrType);

                if (!isSoftDeletable && !isTenantScoped)
                {
                    continue;
                }

                var parameter = Expression.Parameter(entityType.ClrType, "e");
                Expression? predicate = null;

                if (isSoftDeletable)
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(nameof(BaseEntity.IsDeleted)).HasDefaultValue(false);

                    // e.IsDeleted == false
                    predicate = Expression.Equal(
                        Expression.Property(parameter, nameof(BaseEntity.IsDeleted)),
                        Expression.Constant(false));
                }

                // College is the tenant root: it has no CollegeId FK, its own PK IS the
                // tenant key. Handled here rather than via ICollegeScoped so it still gets
                // one combined filter alongside the soft-delete predicate.
                if (entityType.ClrType == typeof(College))
                {
                    var collegeIdProp = Expression.Property(parameter, nameof(College.CollegeId));
                    var currentId = Expression.Property(
                        Expression.Constant(this), nameof(CurrentCollegeId));

                    // A null CurrentCollegeId means a platform administrator, who is
                    // entitled to see every college. Ordinary users see only their own.
                    var collegePredicate = Expression.OrElse(
                        Expression.Equal(currentId, Expression.Constant(null, typeof(Guid?))),
                        Expression.Equal(
                            Expression.Convert(collegeIdProp, typeof(Guid?)), currentId));

                    predicate = predicate == null
                        ? collegePredicate
                        : Expression.AndAlso(predicate, collegePredicate);
                }

                if (isTenantScoped)
                {
                    // e.CollegeId == this.CurrentCollegeId
                    // Reading CurrentCollegeId off the context instance (rather than a
                    // captured value) is what makes EF treat it as a per-query parameter.
                    var collegeProp = Expression.Property(parameter, nameof(ICollegeScoped.CollegeId));
                    var currentCollege = Expression.Property(
                        Expression.Constant(this), nameof(CurrentCollegeId));

                    Expression tenantPredicate = Expression.Equal(collegeProp, currentCollege);

                    // Templatable entities (grading scales, ordinance rule sets, platform
                    // roles) may also hold platform-wide rows shared by every college.
                    if (typeof(ICollegeTemplatable).IsAssignableFrom(entityType.ClrType))
                    {
                        tenantPredicate = Expression.OrElse(
                            tenantPredicate,
                            Expression.Equal(collegeProp, Expression.Constant(null, typeof(Guid?))));
                    }

                    predicate = predicate == null
                        ? tenantPredicate
                        : Expression.AndAlso(predicate, tenantPredicate);
                }

                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(Expression.Lambda(predicate!, parameter));
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

            // Stamp the tenant on every new row, so no service can forget to set it and
            // silently create data that belongs to nobody (and would then be invisible,
            // because the global filter would never match it again).
            // An explicitly-set CollegeId is respected: provisioning a new college has to
            // write rows for a tenant other than the caller's.
            var currentCollegeId = CurrentCollegeId;
            if (currentCollegeId.HasValue)
            {
                foreach (var scopedEntry in ChangeTracker.Entries<ICollegeScoped>()
                             .Where(e => e.State == EntityState.Added && e.Entity.CollegeId == null))
                {
                    scopedEntry.Entity.CollegeId = currentCollegeId;
                }
            }

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