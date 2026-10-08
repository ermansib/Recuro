using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Recuro.Bgv.Infrastructure.Persistence;
using Recuro.BuildingBlocks.Web.Auth;
using Testcontainers.PostgreSql;

namespace Recuro.Bgv.IntegrationTests;

/// <summary>The real API on a real PostgreSQL (Testcontainers), with Config, Vendor, Workflow and Identity stubbed.</summary>
public sealed class BgvApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    public static readonly Guid TenantB = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public StubServices Stubs { get; } = new();

    public MutableClock Clock { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BgvDbContext>().Database.MigrateAsync();
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
        builder.UseSetting("ConnectionStrings:bgv", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Services:Config:RetryAfter", "00:00:00.001");
        builder.UseSetting("Services:Config:CacheFor", "00:00:00.001");
        builder.UseSetting("Services:Identity:CacheFor", "00:00:00.001");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => Stubs));
        });
    }
}

/// <summary>A clock tests can move forward.</summary>
public sealed class MutableClock : TimeProvider
{
    private TimeSpan _offset;

    public void Advance(TimeSpan by) => _offset += by;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;
}
