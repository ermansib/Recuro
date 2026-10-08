using Recuro.BuildingBlocks.Domain;

namespace Recuro.Employee.Domain.Requisitions;

/// <summary>
/// Whether a requisition is open for sourcing, as the Requisition service last reported it
/// (<c>recruitment.sourcing.unlocked</c> opens it, <c>recruitment.mrf.cancelled</c> closes it for good).
/// <see cref="UnlockedAt"/> opens the IJP window (FRD §9.2.1).
/// </summary>
public sealed class SourcingGate : Entity, ITenantOwned
{
    private SourcingGate()
    {
    }

    public Guid TenantId { get; private set; }

    public string ReqId { get; private set; } = string.Empty;

    public bool IsOpen { get; private set; }

    public bool WasCancelled { get; private set; }

    /// <summary>When sourcing was first unlocked. A later redelivery does not move it.</summary>
    public DateTimeOffset? UnlockedAt { get; private set; }

    public static SourcingGate Opened(string reqId, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        ReqId = reqId,
        IsOpen = true,
        UnlockedAt = at,
    };

    public static SourcingGate Cancelled(string reqId) => new()
    {
        Id = Guid.CreateVersion7(),
        ReqId = reqId,
        WasCancelled = true,
    };

    public void Open(DateTimeOffset at)
    {
        // Cancelled is terminal (FRD §6.1): a late unlock never reopens it.
        if (WasCancelled)
        {
            return;
        }

        IsOpen = true;
        UnlockedAt ??= at;
    }

    public void Cancel() => (IsOpen, WasCancelled) = (false, true);
}
