using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace ExamAPI.Services.Tenancy
{
    /// <inheritdoc cref="ICurrentUser"/>
    public class CurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly TenantScope? _scope;

        public CurrentUser(IHttpContextAccessor httpContextAccessor, TenantScope? scope = null)
        {
            _httpContextAccessor = httpContextAccessor;
            _scope = scope;
        }

        private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

        /// <summary>A scope made to act for one college (<see cref="TenantScope"/>) wins; otherwise the signed-in user's.</summary>
        public Guid? CollegeId =>
            _scope?.CollegeId ?? (Guid.TryParse(Principal?.FindFirstValue("CollegeId"), out var id) ? id : null);

        public Guid? UserId =>
            Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        public bool IsPlatformAdmin =>
            string.Equals(Principal?.FindFirstValue("IsPlatformAdmin"), "true", StringComparison.OrdinalIgnoreCase);
    }
}
