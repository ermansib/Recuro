using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Domain.Rules;
using Recuro.Interview.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Recuro.Interview.IntegrationTests;

/// <summary>
/// The real API on a real PostgreSQL (Testcontainers), signed in with X-Dev-* headers. Config, Identity,
/// Requisition and Workflow are replaced by in-memory fakes that speak the same contracts.
/// </summary>
public sealed class InterviewApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    public static readonly Guid TenantB = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public FakeJobDescriptions JobDescriptions { get; } = new();

    public FakeWorkflows Workflows { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InterviewDbContext>().Database.MigrateAsync();
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
        builder.UseSetting("ConnectionStrings:interview", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("FeedbackSla:Enabled", "false");
        builder.UseSetting("Services:Config", "http://config.invalid/");
        builder.UseSetting("Services:Identity", "http://identity.invalid/");
        builder.UseSetting("Services:Requisition", "http://requisition.invalid/");
        builder.UseSetting("Services:Workflow", "http://workflow.invalid/");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IInterviewRulesSource>(new FakeRules());
            services.AddSingleton<IJobDescriptions>(JobDescriptions);
            services.AddSingleton<IWorkflowClient>(Workflows);
            services.AddScoped<IAccessDecisions, FakeAccessDecisions>();
        });
    }
}

public sealed class FakeRules : IInterviewRulesSource
{
    public Task<InterviewRules> GetAsync(CancellationToken ct) => Task.FromResult(InterviewRules.FrdDefault);
}

public sealed class FakeJobDescriptions : IJobDescriptions
{
    public Task<JobDescriptionView?> GetAsync(string reqId, CancellationToken ct) =>
        Task.FromResult<JobDescriptionView?>(reqId switch
        {
            "REQ-NOJD" => null,
            "REQ-E" => new JobDescriptionView(reqId, "E", ["credit", "communication"], []),
            _ => new JobDescriptionView(reqId, "M3", ["credit", "risk", "leadership"], ["case-study.pdf"]),
        });
}

/// <summary>The Identity PDP's <c>assessment.submit</c> rule (RoleOrAssignee): HR-TA, or an assigned interviewer.</summary>
public sealed class FakeAccessDecisions(ICurrentUser user) : IAccessDecisions
{
    public Task<bool> AllowedAsync(string action, string resourceType, string resourceId, IReadOnlyList<string> assigneeIds, CancellationToken ct) =>
        Task.FromResult(user.IsInRole(RecuroRoles.HrTa) || assigneeIds.Contains(user.UserId ?? string.Empty, StringComparer.Ordinal));
}

public sealed class FakeWorkflows : IWorkflowClient
{
    private readonly Lock _gate = new();

    public List<(Guid Id, WorkflowStart Start)> Started { get; } = [];

    public Task<Guid> StartAsync(WorkflowStart start, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        lock (_gate)
        {
            Started.Add((id, start));
        }

        return Task.FromResult(id);
    }

    public Task CancelAsync(Guid instanceId, string reason, CancellationToken ct) => Task.CompletedTask;
}
