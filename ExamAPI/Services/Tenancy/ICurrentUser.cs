namespace ExamAPI.Services.Tenancy
{
    /// <summary>
    /// The tenant identity of the caller, taken from the JWT and nothing else.
    /// <para>
    /// CollegeId must NEVER be read from a request body, query string or route value --
    /// that would let any authenticated user act inside another college. The token is the
    /// single source of truth, and it is set at login from the user's own record.
    /// </para>
    /// </summary>
    public interface ICurrentUser
    {
        /// <summary>Null for anonymous callers and for platform administrators.</summary>
        Guid? CollegeId { get; }

        Guid? UserId { get; }

        /// <summary>
        /// Platform (support/sales/dev) staff. They belong to no college and are the only
        /// principals permitted to bypass the global tenant filter, via explicit
        /// IgnoreQueryFilters() in the platform services.
        /// </summary>
        bool IsPlatformAdmin { get; }
    }
}
