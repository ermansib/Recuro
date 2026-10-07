using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Config.Application.RuleSets.Commands;
using Recuro.Config.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Recuro.Config.IntegrationTests;

/// <summary>The real API on a real PostgreSQL (Testcontainers), signed in with X-Dev-* headers.</summary>
public sealed class ConfigApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    public static readonly Guid TenantB = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ConfigDbContext>().Database.MigrateAsync();

        // Tenant A gets the FRD §5 seed, like a local tenant; tenant B has no rules at all.
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(TenantA);
        await scope.ServiceProvider.GetRequiredService<ICommandHandler<SeedDefaultRuleSetsCommand, IReadOnlyList<string>>>()
            .Handle(new SeedDefaultRuleSetsCommand(), CancellationToken.None);
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
        builder.UseSetting("ConnectionStrings:config", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
    }
}
