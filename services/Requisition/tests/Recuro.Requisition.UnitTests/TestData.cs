using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    public static RequisitionDetails CompleteDetails(bool outOfBudget = false, string grade = "M3") => new(
        "Credit & Risk",
        "Senior Manager — Credit",
        grade,
        "HQ — Mumbai",
        1,
        "N. Sharma — DVP, Credit",
        EmploymentType.Permanent,
        RequisitionNature.NewPosition,
        string.Empty,
        new DateOnly(2026, 12, 1),
        "₹18L – ₹24L",
        outOfBudget,
        outOfBudget ? "Expansion of the Pune collections hub approved by the board." : string.Empty,
        "CA / MBA-Finance; 6–9 years credit appraisal in BFSI.",
        ["Employee Referral", "Job Portals"]);

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
        [new RouteLeg("Approving", [new RouteAssignee(approverRole, "HR Head")], 2, [new RouteEscalation("mdceo", "MD/CEO", 1)])]);
}

internal sealed class FakeRules : IRulesClient
{
    public DoaResolution? Resolution { get; set; } = TestData.Doa();

    public Task<DoaResolution?> ResolveDoaAsync(string grade, bool outOfBudget, DateTimeOffset at, CancellationToken ct) => Task.FromResult(Resolution);

    public Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, string? location, string? versionId, CancellationToken ct) =>
        Task.FromResult(from.AddDays(days));
}

internal sealed class FakeWorkflows : IWorkflowClient
{
    public List<WorkflowStart> Started { get; } = [];

    public List<Guid> Cancelled { get; } = [];

    public bool FailOnStart { get; set; }

    public Task<Guid> StartAsync(WorkflowStart start, CancellationToken ct)
    {
        if (FailOnStart)
        {
            throw new DependencyUnavailableException("workflow down");
        }

        Started.Add(start);
        return Task.FromResult(Guid.NewGuid());
    }

    public Task CancelAsync(Guid instanceId, string reason, CancellationToken ct)
    {
        Cancelled.Add(instanceId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeReqIds : IReqIdAllocator
{
    private int _next;

    public Task<string> NextAsync(int year, CancellationToken ct) => Task.FromResult($"REQ-{year}-{++_next:0000}");
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    /// <summary>Throw on this save number (1-based), to simulate a commit failure.</summary>
    public int? FailOnSave { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Saves++;
        if (Saves == FailOnSave)
        {
            throw new InvalidOperationException("commit failed");
        }

        return Task.FromResult(1);
    }
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
