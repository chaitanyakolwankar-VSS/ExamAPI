namespace ExamAPI.Models
{
    /// <summary>
    /// Marks an entity as belonging to a single college (tenant).
    /// <para>
    /// ApplicationDbContext applies a global query filter to every implementer, so these
    /// types can never be read across tenants by accident, and SaveChangesAsync stamps
    /// CollegeId on insert so callers cannot forget it.
    /// </para>
    /// <para>
    /// The column is nullable for a reason. Some of these tables carry PLATFORM-LEVEL rows
    /// that are shared by every college -- grading scales and ordinance rule sets defined by
    /// the university rather than the college, and platform-admin roles. A null CollegeId
    /// means "belongs to the platform, visible to all". See
    /// <see cref="ApplicationDbContextTenantExtensions"/> for which entities honour that.
    /// </para>
    /// <para>
    /// Only entities that are QUERIED DIRECTLY implement this. Child rows reached solely
    /// through a parent (StudentMarks, SubjectCredits, Rule, RuleAction, RuleCondition,
    /// GradeThreshold, RolePermission, UserPermission, PasswordResetOTP) inherit isolation
    /// from that parent and deliberately carry no column -- EF Core global query filters
    /// cannot traverse navigation properties, which is why the direct parents need one.
    /// </para>
    /// </summary>
    public interface ICollegeScoped
    {
        Guid? CollegeId { get; set; }
    }

    /// <summary>
    /// Implemented by the subset of <see cref="ICollegeScoped"/> entities that may hold
    /// platform-wide template rows (CollegeId == null) visible to every tenant.
    /// </summary>
    public interface ICollegeTemplatable : ICollegeScoped
    {
    }
}
