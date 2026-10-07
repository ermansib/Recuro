using Recuro.Workflow.Application.Abstractions;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    public static readonly Actor HrHead = new("u-hrhead", "K. Mehta", ["hrhead"]);

    public static readonly Actor MdCeo = new("u-md", "R. Iyer", ["mdceo"]);

    public static readonly Actor Initiator = new("u-hrta", "A. Sharma", ["hrta"]);

    public static Presentation Presentation() => new(
        "MRF",
        "default",
        "MRF Approval — REQ-2026-0156",
        "Senior Manager — Credit · HQ — Mumbai",
        "Route: HOD → Function Head → HR Head",
        new SensitiveValue("Band", "₹18L – ₹24L"),
        [
            new InboxAction("approve", "Approve", "primary", ActionEffect.Resolve, "Approved"),
            new InboxAction("reject", "Reject", "danger", ActionEffect.Reject, null),
            new InboxAction("query", "Raise query", "ghost", ActionEffect.Query, null),
        ],
        null);

    public static LegDefinition Leg(string role, int? sla = 2, params LegEscalation[] escalation) =>
        new($"Leg {role}", [new LegAssignee(role, role.ToUpperInvariant())], sla, escalation);

    public static TaskSchedule Schedule(int slaDays = 2, params int[] escalationDays) =>
        new(Now.AddDays(slaDays), escalationDays.Select(d => Now.AddDays(slaDays + d)).ToList());

    public static WorkflowInstance Start(params LegDefinition[] legs)
    {
        var route = legs.Length == 0 ? [Leg("hrhead")] : legs;
        var result = WorkflowInstance.Start(
            "MRF",
            "Requisition",
            "REQ-2026-0156",
            "requisition:abc:1",
            "doa-2026.08",
            route,
            Presentation(),
            Initiator,
            Schedule(2, 1),
            Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}

/// <summary>Adds calendar days; enough to check how the planner composes the calendar.</summary>
internal sealed class FakeCalendar : IBusinessCalendar
{
    public List<(DateOnly From, int Days, string? Version)> Calls { get; } = [];

    public Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, string? versionId, CancellationToken ct)
    {
        Calls.Add((from, days, versionId));
        return Task.FromResult(from.AddDays(days));
    }
}
