using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Recuro.Requisition.IntegrationTests;

/// <summary>
/// The real API on a real PostgreSQL (Testcontainers), signed in with X-Dev-* headers. Config and
/// Workflow are replaced by in-memory fakes that speak the same contracts.
/// </summary>
public sealed class RequisitionApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    public static readonly Guid TenantB = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public FakeRules Rules { get; } = new();

    public FakeWorkflows Workflows { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RequisitionDbContext>().Database.MigrateAsync();
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
        builder.UseSetting("ConnectionStrings:requisition", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Services:Config", "http://config.invalid/");
        builder.UseSetting("Services:Workflow", "http://workflow.invalid/");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IRulesClient>(Rules);
            services.AddSingleton<IWorkflowClient>(Workflows);
        });
    }
}

public sealed class FakeRules : IRulesClient
{
    public static DoaResolution Doa(string approverRole = "hrhead") => new(
        "doa-2026.08",
        "M3",
        "in",
        "HOD",
        "Function Head",
        "HR Head",
        approverRole,
        "₹18L – ₹24L",
        new OverallTat(25, 35, "25–35 wd (managerial)"),
        [new RouteLeg("Approving", [new RouteAssignee(approverRole, "HR Head")], 2, [])]);

    public Task<DoaResolution?> ResolveDoaAsync(string grade, bool outOfBudget, DateTimeOffset at, CancellationToken ct) =>
        Task.FromResult<DoaResolution?>(grade == "ZZ" ? null : Doa(outOfBudget ? "mdceo" : "hrhead"));

    public Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, string? location, string? versionId, CancellationToken ct) =>
        Task.FromResult(from.AddDays(days));
}

public sealed class FakeWorkflows : IWorkflowClient
{
    private readonly Lock _gate = new();

    public List<WorkflowStart> Started { get; } = [];

    /// <summary>Designations whose submit fails at the workflow step.</summary>
    public HashSet<string> FailFor { get; } = [];

    public Task<Guid> StartAsync(WorkflowStart start, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (FailFor.Any(d => start.Presentation.Meta.StartsWith(d, StringComparison.Ordinal)))
        {
            throw new DependencyUnavailableException("workflow down");
        }

        lock (_gate)
        {
            Started.Add(start);
        }

        return Task.FromResult(Guid.NewGuid());
    }

    public Task CancelAsync(Guid instanceId, string reason, CancellationToken ct) => Task.CompletedTask;
}
