using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Recuro.Audit.Infrastructure.Persistence;
using Recuro.BuildingBlocks.Web.Auth;
using Testcontainers.PostgreSql;

namespace Recuro.Audit.IntegrationTests;

/// <summary>The real API on a real PostgreSQL (Testcontainers), signed in with X-Dev-* headers.</summary>
public sealed class AuditApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    public static readonly Guid TenantB = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.MigrateAsync();
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
        builder.UseSetting("ConnectionStrings:audit", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("AuditSeal:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
    }
}
