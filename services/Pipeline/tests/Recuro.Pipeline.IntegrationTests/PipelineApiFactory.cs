using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Pipeline.Application.Abstractions;
using Recuro.Pipeline.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Recuro.Pipeline.IntegrationTests;

/// <summary>The real API on a real PostgreSQL (Testcontainers), with the Candidate service and the clock faked.</summary>
public sealed class PipelineApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    public static readonly Guid TenantB = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public FakeCandidates Candidates { get; } = new();

    public MutableClock Clock { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PipelineDbContext>().Database.MigrateAsync();
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
        builder.UseSetting("ConnectionStrings:pipeline", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("TatScan:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICandidateDirectory>();
            services.AddSingleton<ICandidateDirectory>(Candidates);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>Stands in for the Candidate service: known ids return a frontend-shaped candidate.</summary>
public sealed class FakeCandidates : ICandidateDirectory
{
    private readonly ConcurrentDictionary<string, JsonElement> _candidates = new(StringComparer.Ordinal);

    public string Add(string name = "Rahul Mehta")
    {
        var id = Guid.CreateVersion7().ToString();
        _candidates[id] = JsonSerializer.SerializeToElement(new { id, name, email = "r***@email.example", currentCtc = (decimal?)null });
        return id;
    }

    public Task<bool> ExistsAsync(string candidateId, CancellationToken ct) => Task.FromResult(_candidates.ContainsKey(candidateId));

    public Task<IReadOnlyDictionary<string, JsonElement>> GetManyAsync(IReadOnlyCollection<string> candidateIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
            candidateIds.Where(_candidates.ContainsKey).Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => _candidates[id], StringComparer.Ordinal));
}

/// <summary>A clock tests can move forward.</summary>
public sealed class MutableClock : TimeProvider
{
    private TimeSpan _offset;

    public void Advance(TimeSpan by) => _offset += by;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;
}
