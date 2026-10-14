using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Web;

namespace Recuro.Onboarding.IntegrationTests;

/// <summary>
/// Stands in for Config (resolve endpoints), Candidate (name) and Identity (people, per tenant), routed by path. Like the real Config
/// today, it has no onboarding matrix and refuses negative working days unless a test says otherwise.
/// Records the role each call carried.
/// </summary>
public sealed class StubServices : HttpMessageHandler
{
    public bool ConfigDown { get; set; }

    /// <summary>When set, Config serves an onboarding matrix with this version id and a three-item checklist.</summary>
    public string? OnboardingMatrixVersion { get; set; }

    /// <summary>When true, Config counts working days backwards (weekends only) like the requested contract.</summary>
    public bool BackwardWorkingDays { get; set; }

    public ConcurrentQueue<string?> CandidateCallRoles { get; } = new();

    public ConcurrentQueue<string?> ConfigCallRoles { get; } = new();

    public bool IdentityDown { get; set; }

    private readonly ConcurrentDictionary<(string Tenant, string Id), (string Name, string Department, bool Hod)> _people = new();

    /// <summary>Puts a person on record in Identity for one tenant, optionally as their department's head.</summary>
    public void AddPerson(Guid tenant, string id, string name, string department, bool hod = false) =>
        _people[(tenant.ToString(), id)] = (name, department, hod);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.RequestUri!.AbsolutePath;
        var role = request.Headers.TryGetValues("X-Dev-Roles", out var roles) ? roles.FirstOrDefault() : null;

        if (path.StartsWith("/api/v1/resolve/", StringComparison.Ordinal))
        {
            ConfigCallRoles.Enqueue(role);
            if (ConfigDown)
            {
                throw new HttpRequestException("Config is down (test).");
            }

            return Task.FromResult(Config(path, HttpUtility.ParseQueryString(request.RequestUri.Query)));
        }

        if (path.StartsWith("/api/v1/identity/users", StringComparison.Ordinal))
        {
            if (IdentityDown)
            {
                throw new HttpRequestException("Identity is down (test).");
            }

            var tenant = request.Headers.TryGetValues("X-Dev-Tenant", out var tenants) ? tenants.FirstOrDefault() ?? string.Empty : string.Empty;
            return Task.FromResult(Identity(tenant, path, HttpUtility.ParseQueryString(request.RequestUri.Query)));
        }

        if (path.StartsWith("/api/v1/candidates/", StringComparison.Ordinal))
        {
            CandidateCallRoles.Enqueue(role);
            return Task.FromResult(Json(new { id = path.Split('/')[4], name = "J. Nair", email = "j.nair@example.test" }));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private HttpResponseMessage Config(string path, System.Collections.Specialized.NameValueCollection query)
    {
        if (path == "/api/v1/resolve/working-days")
        {
            var from = DateOnly.Parse(query["from"]!, CultureInfo.InvariantCulture);
            var days = int.Parse(query["days"]!, CultureInfo.InvariantCulture);
            if (days < 0 && !BackwardWorkingDays)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            return Json(new { date = Weekdays(from, days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), configVersionId = Guid.Empty });
        }

        if (path == "/api/v1/resolve/matrices/onboarding" && OnboardingMatrixVersion is { } version)
        {
            return Json(new
            {
                configVersionId = version,
                matrixType = "Onboarding",
                number = 3,
                effectiveFrom = "2026-01-01T00:00:00Z",
                content = new
                {
                    checklist = new[]
                    {
                        new { key = "laptop", label = "Laptop issued" },
                        new { key = "badge", label = "Badge issued" },
                        new { key = "induction", label = "Induction booked" },
                    },
                    probationMonths = 3,
                },
            });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private HttpResponseMessage Identity(string tenant, string path, System.Collections.Specialized.NameValueCollection query)
    {
        object Dto(string id, (string Name, string Department, bool Hod) p) =>
            new { id, tenantId = tenant, name = p.Name, role = p.Hod ? "employee" : "hrta", department = p.Department, managerId = string.Empty };

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 5)
        {
            var id = Uri.UnescapeDataString(segments[4]);
            return _people.TryGetValue((tenant, id), out var person) ? Json(Dto(id, person)) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var department = query["department"];
        if (department is not null && (department.Length == 0 || department.Any(ch => char.IsUpper(ch) || char.IsWhiteSpace(ch))))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        var heads = _people
            .Where(p => p.Key.Tenant == tenant && p.Value.Hod && query["role"] == "hod" && p.Value.Department == department)
            .OrderBy(p => p.Value.Name, StringComparer.Ordinal)
            .Select(p => Dto(p.Key.Id, p.Value))
            .ToList();
        return Json(heads);
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    /// <summary>Weekdays forwards (days &gt; 0) or backwards (days &lt; 0), the start day never counting.</summary>
    public static DateOnly Weekdays(DateOnly from, int days)
    {
        var step = Math.Sign(days);
        var day = from;
        for (var counted = 0; counted < Math.Abs(days);)
        {
            day = day.AddDays(step);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                counted++;
            }
        }

        return day;
    }
}
