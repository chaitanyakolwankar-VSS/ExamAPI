namespace ExamAPI.Services.StudentAccess
{
    /// <summary>
    /// The address of the student site that links are written with. StudentAccess:AppUrl when it is set; otherwise
    /// the address the staff member's browser is on, accepted only when its origin is one this API already trusts
    /// (Cors:AllowedOrigins, or localhost while developing) -- so a link can never point at another site.
    /// </summary>
    public sealed class StudentSiteUrl
    {
        private readonly string? _configured;
        private readonly HashSet<string> _origins;
        private readonly bool _development;

        public StudentSiteUrl(IConfiguration config, IHostEnvironment env)
        {
            _configured = config["StudentAccess:AppUrl"];
            _origins = new HashSet<string>(
                (config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>()).Select(o => o.TrimEnd('/')),
                StringComparer.OrdinalIgnoreCase);
            _development = env.IsDevelopment();
        }

        public StudentSiteUrl(string? configured, IEnumerable<string> origins, bool development)
        {
            _configured = configured;
            _origins = new HashSet<string>(origins.Select(o => o.TrimEnd('/')), StringComparer.OrdinalIgnoreCase);
            _development = development;
        }

        /// <summary>The base address (no trailing slash), or null when none can be trusted.</summary>
        public string? Resolve(string? fromBrowser)
        {
            if (!string.IsNullOrWhiteSpace(_configured))
                return Clean(_configured);
            if (string.IsNullOrWhiteSpace(fromBrowser) || !Uri.TryCreate(fromBrowser.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                return null;
            var origin = uri.GetLeftPart(UriPartial.Authority);
            var trusted = _origins.Contains(origin) || (_development && uri.IsLoopback);
            if (!trusted || (uri.Scheme == "http" && !uri.IsLoopback))
                return null;
            return Clean(uri.GetLeftPart(UriPartial.Path));
        }

        private static string Clean(string url) => url.Trim().TrimEnd('/');
    }
}
