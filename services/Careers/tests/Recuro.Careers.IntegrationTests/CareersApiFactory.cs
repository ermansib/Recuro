using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Careers.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Recuro.Careers.IntegrationTests;

/// <summary>The real API on a real PostgreSQL (Testcontainers), with Candidate, Pipeline and Config stubbed at the HTTP edge.</summary>
public sealed class CareersApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    public static readonly Guid TenantB = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public StubServices Downstream { get; } = new();

    public MutableClock Clock { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CareersDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public HttpClient ClientFor(Guid tenant, string role, string user = "u-1", string name = "A. Sharma")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.UserHeader, user);
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.NameHeader, name);
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.RolesHeader, role);
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.TenantHeader, tenant.ToString());
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:careers", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Careers:SearchCacheFor", "00:00:00.001");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => Downstream));
        });
    }
}

/// <summary>
/// Candidate, Pipeline and Config as seen over HTTP. Records who called (to prove intake calls go out as
/// the service) and lets tests make Pipeline refuse.
/// </summary>
public sealed class StubServices : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, string> _candidatesByEmail = new(StringComparer.OrdinalIgnoreCase);
    private int _appNumber;

    public ConcurrentBag<string> Tombstoned { get; } = [];

    public ConcurrentBag<(string Path, string? Roles, string? Tenant)> Calls { get; } = [];

    /// <summary>When set, Pipeline answers 409 with this code.</summary>
    public string? PipelineRefuses { get; set; }

    public int PipelineCreates => Calls.Count(c => c.Path == "/api/v1/pipeline/applications");

    /// <summary>A person already on file in the Candidate service.</summary>
    public string Existing(string email) => _candidatesByEmail.GetOrAdd(email, _ => Guid.CreateVersion7().ToString());

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.RequestUri!.AbsolutePath;
        Calls.Add((path, Header(request, DevelopmentAuthenticationHandler.RolesHeader), Header(request, DevelopmentAuthenticationHandler.TenantHeader)));

        if (path == "/api/v1/candidates" && request.Method == HttpMethod.Post)
        {
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var email = body.GetProperty("email").GetString()!;
            if (_candidatesByEmail.TryGetValue(email, out var existing))
            {
                return Problem(HttpStatusCode.Conflict, "duplicate_candidate", $"A candidate with the same email or phone already exists ({existing}).");
            }

            var id = Existing(email);
            return Json(HttpStatusCode.Created, new { id, name = body.GetProperty("name").GetString() });
        }

        if (path.StartsWith("/api/v1/candidates/", StringComparison.Ordinal) && path.EndsWith("/tombstone", StringComparison.Ordinal))
        {
            Tombstoned.Add(path.Split('/')[4]);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (path == "/api/v1/pipeline/applications")
        {
            if (PipelineRefuses is { } code)
            {
                return Problem(HttpStatusCode.Conflict, code, "Refused (test).");
            }

            var number = Interlocked.Increment(ref _appNumber);
            return Json(HttpStatusCode.Created, new { appId = string.Create(CultureInfo.InvariantCulture, $"APP-2026-{number:D4}"), stage = "Sourced" });
        }

        if (path == "/api/v1/resolve/working-days")
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
            var date = AddWeekdays(DateOnly.Parse(query["from"]!, CultureInfo.InvariantCulture), int.Parse(query["days"]!, CultureInfo.InvariantCulture));
            return Json(HttpStatusCode.OK, new { date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), configVersionId = Guid.Empty });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static DateOnly AddWeekdays(DateOnly from, int days)
    {
        var day = from;
        for (var added = 0; added < days;)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                added++;
            }
        }

        return day;
    }

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? string.Join(',', values) : null;

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status) { Content = JsonContent.Create(body) };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code, string title) =>
        Json(status, new { status = (int)status, title, code });
}

/// <summary>A clock tests can move forward.</summary>
public sealed class MutableClock : TimeProvider
{
    private TimeSpan _offset;

    public void Advance(TimeSpan by) => _offset += by;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;
}
