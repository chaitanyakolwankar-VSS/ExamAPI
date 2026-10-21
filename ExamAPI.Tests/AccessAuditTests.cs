using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExamAPI.Models;
using ExamAPI.Services.Auth;
using ExamAPI.Services.StudentAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ExamAPI.Tests;

/// <summary>
/// Student access, step 7: the authorization audit, run through the real pipeline (routing, authentication schemes,
/// authorization policies, rate limiter) rather than by reading attributes. Every endpoint of the API is listed from
/// the routing table and sorted as anonymous, student (the Student policy) or staff (everything else); then each kind
/// is called with the wrong and the right credentials. Staff endpoints must refuse no sign-in and a student session;
/// student endpoints must refuse no sign-in, a staff token, an ended session and a session of a stopped link; a
/// student can only reach their own link's data; one college's staff cannot see or stop another college's links.
/// </summary>
public sealed class AccessAuditTests : IClassFixture<AccessAuditFactory>
{
    private enum Kind { Anonymous, Student, Staff }

    private sealed record Route(string Method, string Url, string Pattern, Kind Kind, RouteEndpoint Endpoint)
    {
        public override string ToString() => $"{Method} /{Url}";
    }

    private readonly AccessAuditFactory _factory;
    private readonly ITestOutputHelper _output;
    private readonly HttpClient _client;

    public AccessAuditTests(AccessAuditFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
        factory.EnsureSeeded();
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    // ------------------------------------------------------------------ the endpoint list

    private List<Route> AllRoutes()
    {
        var routes = new List<Route>();
        foreach (var endpoint in _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var kind = Classify(endpoint);
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.ToList() ?? new List<string>();
            if (methods.Count == 0) methods.Add("GET");
            var url = BuildUrl(endpoint.RoutePattern);
            foreach (var method in methods.Distinct(StringComparer.OrdinalIgnoreCase))
                routes.Add(new Route(method.ToUpperInvariant(), url, endpoint.RoutePattern.RawText ?? url, kind, endpoint));
        }
        return routes.OrderBy(r => r.Url, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal).ToList();
    }

    private static Kind Classify(RouteEndpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null) return Kind.Anonymous;
        if (endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == StudentAccessAuth.Policy)) return Kind.Student;
        return Kind.Staff;
    }

    /// <summary>A concrete address for the pattern: guid -> a new guid, int -> 1, regex(^(a|b)$) -> a, anything else -> x.</summary>
    private static string BuildUrl(RoutePattern pattern)
    {
        var url = new StringBuilder();
        var first = true;
        foreach (var segment in pattern.PathSegments)
        {
            if (!first) url.Append('/');
            first = false;
            foreach (var part in segment.Parts)
            {
                switch (part)
                {
                    case RoutePatternLiteralPart literal: url.Append(literal.Content); break;
                    case RoutePatternSeparatorPart separator: url.Append(separator.Content); break;
                    case RoutePatternParameterPart parameter: url.Append(ValueFor(parameter)); break;
                }
            }
        }
        return url.ToString();
    }

    private static string ValueFor(RoutePatternParameterPart parameter)
    {
        var policies = parameter.ParameterPolicies.Select(p => p.Content ?? "").ToList();
        if (policies.Contains("guid")) return Guid.NewGuid().ToString();
        if (policies.Contains("int") || policies.Contains("long")) return "1";
        var regex = policies.FirstOrDefault(p => p.StartsWith("regex(", StringComparison.Ordinal));
        if (regex != null)
        {
            var body = regex["regex(".Length..^1].TrimStart('^').TrimEnd('$');
            if (body.StartsWith('(') && body.EndsWith(')')) body = body[1..^1];
            return body.Split('|')[0];
        }
        return "x";
    }

    // ------------------------------------------------------------------ sending

    private async Task<(HttpStatusCode? Status, string Note)> SendAsync(Route route, string? bearer = null, string? session = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(route.Method), "/" + route.Url);
        if (route.Method is "POST" or "PUT" or "PATCH")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (session != null) request.Headers.Add(StudentAccessAuth.Header, session);
        try
        {
            using var response = await _client.SendAsync(request);
            return (response.StatusCode, "");
        }
        catch (Exception ex)
        {
            return (null, "threw " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string Describe(Route route, HttpStatusCode? status, string note) =>
        $"{route}  ->  {(status.HasValue ? $"{(int)status.Value} {status.Value}" : note)}";

    private static void AssertNone(List<string> failures, string what)
    {
        Assert.True(failures.Count == 0, $"{what}: {failures.Count} problem(s):{Environment.NewLine}" + string.Join(Environment.NewLine, failures));
    }

    private List<Route> Of(Kind kind) => AllRoutes().Where(r => r.Kind == kind).ToList();

    // ------------------------------------------------------------------ a) every endpoint is covered

    [Fact]
    public void Every_endpoint_is_sorted_as_anonymous_student_or_staff_and_gets_a_testable_address()
    {
        var routes = AllRoutes();
        _output.WriteLine($"{routes.Count} endpoint/method pairs: " +
            $"{routes.Count(r => r.Kind == Kind.Staff)} staff, {routes.Count(r => r.Kind == Kind.Student)} student, {routes.Count(r => r.Kind == Kind.Anonymous)} anonymous.");

        Assert.NotEmpty(routes);
        Assert.Contains(routes, r => r.Kind == Kind.Staff);
        Assert.Contains(routes, r => r.Kind == Kind.Student);
        Assert.Contains(routes, r => r.Kind == Kind.Anonymous);

        var bad = routes.Where(r => r.Url.Contains('{') || r.Url.Contains('}') || r.Url.Contains("//") || string.IsNullOrWhiteSpace(r.Url))
            .Select(r => $"{r}  (pattern {r.Pattern})").ToList();
        AssertNone(bad, "Addresses that could not be built");

        // The student endpoints we expect are really classified as student (the policy name is read from the metadata).
        Assert.Contains(routes, r => r.Pattern == "api/StudentAccess/me" && r.Kind == Kind.Student);
        Assert.Contains(routes, r => r.Pattern == "api/StudentLinks/send" && r.Kind == Kind.Staff);
    }

    // ------------------------------------------------------------------ b) staff endpoints

    [Fact]
    public async Task Staff_endpoints_answer_401_to_a_caller_with_no_sign_in()
    {
        var failures = new List<string>();
        foreach (var route in Of(Kind.Staff))
        {
            var (status, note) = await SendAsync(route);
            if (status != HttpStatusCode.Unauthorized) failures.Add(Describe(route, status, note));
        }
        AssertNone(failures, "Staff endpoints that did not answer 401 with no credentials");
    }

    [Fact]
    public async Task Staff_endpoints_answer_401_to_a_student_session_so_a_student_never_reaches_staff_data()
    {
        var failures = new List<string>();
        foreach (var route in Of(Kind.Staff))
        {
            var (status, note) = await SendAsync(route, session: _factory.SessionA);
            if (status != HttpStatusCode.Unauthorized) failures.Add(Describe(route, status, note));
        }
        AssertNone(failures, "Staff endpoints that did not answer 401 to a valid student session");
    }

    // ------------------------------------------------------------------ c) student endpoints

    [Fact]
    public async Task Student_endpoints_answer_401_with_no_sign_in()
    {
        var failures = new List<string>();
        foreach (var route in Of(Kind.Student))
        {
            var (status, note) = await SendAsync(route);
            if (status != HttpStatusCode.Unauthorized) failures.Add(Describe(route, status, note));
        }
        AssertNone(failures, "Student endpoints that did not answer 401 with no credentials");
    }

    [Fact]
    public async Task Student_endpoints_answer_401_to_a_staff_token_even_a_valid_one()
    {
        var failures = new List<string>();
        foreach (var route in Of(Kind.Student))
        {
            var (status, note) = await SendAsync(route, bearer: _factory.StaffTokenA);
            if (status != HttpStatusCode.Unauthorized) failures.Add(Describe(route, status, note));
        }
        AssertNone(failures, "Student endpoints that did not answer 401 to a valid staff token");
    }

    [Fact]
    public async Task Student_endpoints_answer_401_to_a_session_that_has_ended()
    {
        var failures = new List<string>();
        foreach (var route in Of(Kind.Student))
        {
            var (status, note) = await SendAsync(route, session: _factory.SessionEnded);
            if (status != HttpStatusCode.Unauthorized) failures.Add(Describe(route, status, note));
        }
        AssertNone(failures, "Student endpoints that did not answer 401 to an ended session");
    }

    [Fact]
    public async Task Student_endpoints_answer_401_to_a_session_whose_link_was_stopped()
    {
        var failures = new List<string>();
        foreach (var route in Of(Kind.Student))
        {
            var (status, note) = await SendAsync(route, session: _factory.SessionRevokedLink);
            if (status != HttpStatusCode.Unauthorized) failures.Add(Describe(route, status, note));
        }
        AssertNone(failures, "Student endpoints that did not answer 401 to a session of a revoked link");
    }

    [Fact]
    public async Task Student_endpoints_let_a_valid_session_in()
    {
        var failures = new List<string>();
        foreach (var route in Of(Kind.Student))
        {
            // A fresh session each time: some endpoints sign the student out or use the link up.
            var (status, note) = await SendAsync(route, session: _factory.NewAuditSession());
            if (status is null or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) failures.Add(Describe(route, status, note));
        }
        AssertNone(failures, "Student endpoints that refused a valid session (401/403) or threw");
    }

    // ------------------------------------------------------------------ d) no record ids

    [Fact]
    public void Student_endpoints_take_no_record_id_only_fixed_words_so_a_student_can_reach_only_their_own_link()
    {
        var fixedWords = new Regex(@"^regex\(\^\(([A-Za-z]+)(\|[A-Za-z]+)*\)\$\)$");
        var failures = new List<string>();
        foreach (var route in Of(Kind.Student).GroupBy(r => r.Pattern).Select(g => g.First()))
        {
            foreach (var parameter in route.Endpoint.RoutePattern.Parameters)
            {
                var policies = parameter.ParameterPolicies.Select(p => p.Content ?? "").ToList();
                if (!policies.Any(p => fixedWords.IsMatch(p)))
                    failures.Add($"/{route.Pattern}: parameter '{parameter.Name}' is not limited to fixed words (constraints: {string.Join(", ", policies)})");
            }
        }
        AssertNone(failures, "Student routes with a free route parameter");
    }

    // ------------------------------------------------------------------ e) own record only

    [Fact]
    public async Task A_student_session_shows_only_that_students_own_record()
    {
        Assert.Equal(AccessAuditFactory.StudentIdSA1, await MeAsync(_factory.SessionA));
        Assert.Equal(AccessAuditFactory.StudentIdSB1, await MeAsync(_factory.SessionB));
        Assert.NotEqual(AccessAuditFactory.StudentIdSA2, await MeAsync(_factory.SessionA));
    }

    private async Task<string?> MeAsync(string session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/StudentAccess/me");
        request.Headers.Add(StudentAccessAuth.Header, session);
        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET api/StudentAccess/me answered {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("student").GetProperty("studentId").GetString();
    }

    // ------------------------------------------------------------------ f) another college's staff

    [Fact]
    public async Task Staff_of_one_college_cannot_open_another_colleges_batch_but_its_own_staff_can()
    {
        var url = $"/api/StudentLinks/batches/{_factory.BatchA}";

        var other = await GetAsync(url, _factory.StaffTokenB);
        Assert.Equal(HttpStatusCode.NotFound, other.Status);

        var own = await GetAsync(url, _factory.StaffTokenA);
        Assert.True(own.Status == HttpStatusCode.OK, $"College A's own staff got {(int)own.Status}: {own.Body}");
    }

    [Fact]
    public async Task Staff_of_one_college_cannot_stop_another_colleges_link()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/StudentLinks/links/{_factory.LinkA}/stop");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _factory.StaffTokenB);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
            $"Expected 404 (or 400) but got {(int)response.StatusCode}.");
        Assert.Equal(TaskLinkStatuses.Active, _factory.LinkStatus(_factory.LinkA));
    }

    private async Task<(HttpStatusCode Status, string Body)> GetAsync(string url, string bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await _client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------ g) rate limit

    [Fact]
    public void The_three_anonymous_student_site_endpoints_carry_the_student_access_rate_limit()
    {
        var routes = AllRoutes();
        foreach (var name in new[] { "open", "code", "verify" })
        {
            var route = routes.FirstOrDefault(r => r.Pattern == "api/StudentAccess/" + name);
            Assert.NotNull(route);
            Assert.Equal(Kind.Anonymous, route!.Kind);
            var limits = route.Endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>();
            Assert.Contains(limits, l => l.PolicyName == RateLimits.StudentAccess);
        }

        // And no other anonymous student-site endpoint is left unlimited.
        var unlimited = routes
            .Where(r => r.Kind == Kind.Anonymous && r.Pattern.StartsWith("api/StudentAccess/", StringComparison.OrdinalIgnoreCase))
            .Where(r => !r.Endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Any(l => l.PolicyName == RateLimits.StudentAccess))
            .Select(r => r.ToString()).ToList();
        AssertNone(unlimited, "Anonymous student-site endpoints without the student-access rate limit");
    }

    // ------------------------------------------------------------------ h) the open list

    [Fact]
    public void Print_the_endpoints_that_need_no_sign_in()
    {
        var anonymous = Of(Kind.Anonymous);
        _output.WriteLine($"Endpoints open without sign-in ({anonymous.Count}):");
        foreach (var route in anonymous)
        {
            var limits = route.Endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Select(l => l.PolicyName).ToList();
            _output.WriteLine($"  {route}" + (limits.Count > 0 ? $"   [rate limit: {string.Join(", ", limits)}]" : ""));
        }
        Assert.NotEmpty(anonymous);
    }
}
