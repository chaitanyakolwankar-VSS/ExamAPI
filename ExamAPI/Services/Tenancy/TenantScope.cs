namespace ExamAPI.Services.Tenancy
{
    /// <summary>
    /// Lets work that has no signed-in user (a gateway's return, a background job) act for one college inside its own
    /// DI scope, so services built on the tenant filter (e.g. ATKT assignment) see that college's rows and nothing else.
    /// Set it only on a scope created for that purpose; a request's own scope never sets it.
    /// </summary>
    public sealed class TenantScope
    {
        public Guid? CollegeId { get; set; }
    }
}
