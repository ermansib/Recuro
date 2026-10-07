using Recuro.BuildingBlocks.Domain;

namespace Recuro.Pipeline.Domain.Requisitions;

/// <summary>
/// RCU-PPL-001 / RCU-MRF-006: whether a requisition is open for sourcing, as last reported by the
/// Requisition service (<c>recruitment.sourcing.unlocked</c> opens it, <c>recruitment.mrf.cancelled</c>
/// closes it). Kept locally so creating an application needs no call to another service.
/// </summary>
public sealed class SourcingGate : Entity, ITenantOwned
{
    private SourcingGate()
    {
    }

    public Guid TenantId { get; private set; }

    public string ReqId { get; private set; } = string.Empty;

    public bool IsOpen { get; private set; }

    public DateOnly? TargetClosure { get; private set; }

    public bool WasCancelled { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static SourcingGate Opened(string reqId, DateOnly? targetClosure, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ReqId = reqId,
        IsOpen = true,
        TargetClosure = targetClosure,
        UpdatedAt = now,
    };

    public static SourcingGate Cancelled(string reqId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ReqId = reqId,
        IsOpen = false,
        WasCancelled = true,
        UpdatedAt = now,
    };

    public void Open(DateOnly? targetClosure, DateTimeOffset now)
    {
        // A cancelled requisition never reopens (FRD §6.1: Cancelled is terminal).
        if (WasCancelled)
        {
            return;
        }

        (IsOpen, TargetClosure, UpdatedAt) = (true, targetClosure ?? TargetClosure, now);
    }

    public void Cancel(DateTimeOffset now) => (IsOpen, WasCancelled, UpdatedAt) = (false, true, now);
}
