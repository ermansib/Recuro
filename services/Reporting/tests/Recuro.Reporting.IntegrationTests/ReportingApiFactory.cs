using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Application.Reports;
using Recuro.Reporting.Domain.Kpis;
using Recuro.Reporting.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Recuro.Reporting.IntegrationTests;

/// <summary>
/// The real API on a real PostgreSQL (Testcontainers), signed in with X-Dev-* headers. Config and Identity
/// are replaced by in-memory fakes that speak the same contracts.
/// </summary>
public sealed class ReportingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("4c1d7a52-93b0-4f0e-8f5e-2b8f6f1a7c01");
    public static readonly Guid TenantB = Guid.Parse("4c1d7a52-93b0-4f0e-8f5e-2b8f6f1a7c02");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public HttpClient ClientFor(Guid tenant, string role, string user = "u-head", string name = "K. Iyer")
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
        builder.UseSetting("ConnectionStrings:reporting", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("ReportSchedule:Enabled", "false");
        builder.UseSetting("Services:Config", "http://config.invalid/");
        builder.UseSetting("Services:Identity", "http://identity.invalid/");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ITatTargetsSource>(new FakeTatTargets());
            services.AddSingleton<IMaskingMaps>(new FakeMaskingMaps());
        });
    }
}

/// <summary>Config's TAT and DOA matrices: 20 working days overall for every grade, 2 for MRF approval.</summary>
public sealed class FakeTatTargets : ITatTargetsSource
{
    public Task<TatTargets> GetAsync(DateTimeOffset at, CancellationToken ct) => Task.FromResult(new TatTargets(
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["M1"] = 20 },
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["mrf-approval"] = 2, ["overall-managerial"] = 20 },
        "tat-test;doa-test"));
}

/// <summary>Identity's <c>report</c> map as proposed: HR-TA does not see channel spend.</summary>
public sealed class FakeMaskingMaps : IMaskingMaps
{
    public Task<IReadOnlyDictionary<string, string>?> GetAsync(string role, CancellationToken ct) =>
        Task.FromResult(ReportMasking.LocalFallback(role));
}
