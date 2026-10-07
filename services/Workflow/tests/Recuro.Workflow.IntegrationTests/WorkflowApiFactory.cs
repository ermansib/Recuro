using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Workflow.Application.Abstractions;
using Recuro.Workflow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Recuro.Workflow.IntegrationTests;

/// <summary>
/// The real API on a real PostgreSQL (Testcontainers), signed in with X-Dev-* headers. The Config
/// calendar is an in-memory fake; the clock is settable so escalations can be driven.
/// </summary>
public sealed class WorkflowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("7b2e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    public static readonly Guid TenantB = Guid.Parse("7b2e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public HttpClient ClientFor(Guid tenant, string role, string user = "u-1", string name = "K. Mehta")
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
        builder.UseSetting("ConnectionStrings:workflow", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Escalation:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Services:Config", "http://config.invalid/");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IBusinessCalendar, CalendarDays>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

public sealed class MutableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Every day is a working day: deadlines are easy to predict.</summary>
public sealed class CalendarDays : IBusinessCalendar
{
    public Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, string? versionId, CancellationToken ct) =>
        Task.FromResult(from.AddDays(days));
}
