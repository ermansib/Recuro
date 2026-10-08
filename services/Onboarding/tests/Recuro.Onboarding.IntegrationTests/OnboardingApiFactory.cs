using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Onboarding.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Recuro.Onboarding.IntegrationTests;

/// <summary>The real API on a real PostgreSQL (Testcontainers), with Config and Candidate stubbed.</summary>
public sealed class OnboardingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("7b2f4d5f-1c5e-4e66-8a6c-6a1e4c9b1b01");
    public static readonly Guid TenantB = Guid.Parse("7b2f4d5f-1c5e-4e66-8a6c-6a1e4c9b1b02");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string DocumentRoot { get; } = Path.Combine(Path.GetTempPath(), $"recuro-onboarding-{Guid.NewGuid():N}");

    public StubServices Stubs { get; } = new();

    public MutableClock Clock { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OnboardingDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(DocumentRoot))
        {
            Directory.Delete(DocumentRoot, recursive: true);
        }
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
        builder.UseSetting("ConnectionStrings:onboarding", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("MilestoneScheduler:Enabled", "false");
        builder.UseSetting("Services:Config", "http://config.invalid/");
        builder.UseSetting("Services:Candidate", "http://candidate.invalid/");
        builder.UseSetting("Services:CacheFor", "00:00:00.001");
        builder.UseSetting("DocumentStorage:RootPath", DocumentRoot);
        builder.UseSetting("DocumentStorage:ActiveKeyId", "test1");
        builder.UseSetting("DocumentStorage:Keys:test1", "uu+FaSAAY44TzeCokIufEwrGGLFYj7XbN6ziLYZKv6I=");
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
