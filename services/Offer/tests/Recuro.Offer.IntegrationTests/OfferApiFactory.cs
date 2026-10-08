using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Application.Offers;
using Recuro.Offer.Domain.Rules;
using Recuro.Offer.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Recuro.Offer.IntegrationTests;

/// <summary>
/// The real API on a real PostgreSQL (Testcontainers), signed in with X-Dev-* headers. Config, Identity,
/// Requisition and Workflow are replaced by in-memory fakes that speak the same contracts.
/// </summary>
public sealed class OfferApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("7b2f4d5f-1c5e-4e66-8a6c-6a1e4c9b1b01");
    public static readonly Guid TenantB = Guid.Parse("7b2f4d5f-1c5e-4e66-8a6c-6a1e4c9b1b02");

    public const string CallbackSecret = "test-callback-secret";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public FakeWorkflows Workflows { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OfferDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public HttpClient ClientFor(Guid tenant, string role, string user = "u-ta", string name = "A. Sharma")
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
        builder.UseSetting("ConnectionStrings:offer", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("OfferLifecycle:Enabled", "false");
        builder.UseSetting("Letters:SigningKey", "test-signing-key-test-signing-key");
        builder.UseSetting("Letters:CallbackSecret", CallbackSecret);
        builder.UseSetting("Services:Config", "http://config.invalid/");
        builder.UseSetting("Services:Identity", "http://identity.invalid/");
        builder.UseSetting("Services:Requisition", "http://requisition.invalid/");
        builder.UseSetting("Services:Workflow", "http://workflow.invalid/");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IOfferRulesSource>(new FakeRules());
            services.AddSingleton<IWorkingDays>(new FakeWorkingDays());
            services.AddSingleton<IRequisitions>(new FakeRequisitions());
            services.AddSingleton<IMaskingMaps>(new FakeMaskingMaps());
            services.AddSingleton<IWorkflowClient>(Workflows);
        });
    }
}

public sealed class FakeRules : IOfferRulesSource
{
    public static readonly OfferRules Rules = new(
        "offer-v1",
        [
            new OfferApprovalRule(["E", "M1"], new ApprovalLeg("HR-TA → HR Head", "hrhead"), new ApprovalLeg("HR-TA → HR Head (deviation)", "hrhead")),
            new OfferApprovalRule(["M3", "VP"], new ApprovalLeg("HR-TA → HR Head", "hrhead"), new ApprovalLeg("HR-TA → MD/CEO", "mdceo")),
        ],
        CtcRuleSet.Default,
        5,
        3,
        7);

    public Task<OfferRules> GetAsync(CancellationToken ct) => Task.FromResult(Rules);
}

/// <summary>Calendar days stand in for working days.</summary>
public sealed class FakeWorkingDays : IWorkingDays
{
    public Task<DateOnly> AddAsync(DateOnly from, int days, CancellationToken ct) => Task.FromResult(from.AddDays(days));
}

public sealed class FakeRequisitions : IRequisitions
{
    public Task<RequisitionView?> GetAsync(string reqId, CancellationToken ct) =>
        Task.FromResult<RequisitionView?>(reqId == "REQ-NONE" ? null : new RequisitionView(reqId, "Senior Manager — Credit", "M3", "HQ — Mumbai", "N. Sharma"));
}

/// <summary>Identity's offer map as the brief proposes it: MD/CEO sees no CTC; candidates and employees see nothing.</summary>
public sealed class FakeMaskingMaps : IMaskingMaps
{
    public Task<IReadOnlyDictionary<string, string>?> GetAsync(string role, CancellationToken ct) =>
        Task.FromResult(role is RecuroRoles.HrTa or RecuroRoles.HrHead or RecuroRoles.Service
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : OfferMasking.LocalFallback(role));
}

/// <summary>Workflow with one open task per instance, assigned to the instance's only leg role.</summary>
public sealed class FakeWorkflows : IWorkflowClient
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, (WorkflowStart Start, Guid TaskId, string Status)> _instances = [];

    public List<WorkflowStart> Started
    {
        get
        {
            lock (_gate)
            {
                return _instances.Values.Select(i => i.Start).ToList();
            }
        }
    }

    public Task<Guid> StartAsync(WorkflowStart start, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        lock (_gate)
        {
            _instances[id] = (start, Guid.NewGuid(), "Open");
        }

        return Task.FromResult(id);
    }

    public Task CancelAsync(Guid instanceId, string reason, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_instances.TryGetValue(instanceId, out var i))
            {
                _instances[instanceId] = i with { Status = "Cancelled" };
            }
        }

        return Task.CompletedTask;
    }

    public Task<WorkflowInstanceView?> GetAsync(Guid instanceId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_instances.TryGetValue(instanceId, out var i)
                ? new WorkflowInstanceView(instanceId, i.Status == "Open" ? "Pending" : i.Status, [new WorkflowTaskView(i.TaskId, i.Start.Legs[0].Assignees[0].Role, i.Status)])
                : null);
        }
    }

    public Task<WorkflowDecisionResult> DecideAsync(Guid taskId, string actionId, string? reason, CancellationToken ct)
    {
        lock (_gate)
        {
            var (id, instance) = _instances.Single(kv => kv.Value.TaskId == taskId) is var kv ? (kv.Key, kv.Value) : default;
            if (instance.Status != "Open")
            {
                return Task.FromResult(new WorkflowDecisionResult(false, 409, "decided"));
            }

            _instances[id] = instance with { Status = "Completed" };
            return Task.FromResult(new WorkflowDecisionResult(true, 200, null));
        }
    }
}
