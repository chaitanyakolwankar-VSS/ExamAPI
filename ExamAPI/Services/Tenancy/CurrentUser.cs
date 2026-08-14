using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace ExamAPI.Services.Tenancy
{
    /// <inheritdoc cref="ICurrentUser"/>
    public class CurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUser(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

        public Guid? CollegeId =>
            Guid.TryParse(Principal?.FindFirstValue("CollegeId"), out var id) ? id : null;

        public Guid? UserId =>
            Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        public bool IsPlatformAdmin =>
            string.Equals(Principal?.FindFirstValue("IsPlatformAdmin"), "true", StringComparison.OrdinalIgnoreCase);
    }
}
