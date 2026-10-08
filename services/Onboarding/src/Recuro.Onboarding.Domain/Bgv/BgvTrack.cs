using Recuro.BuildingBlocks.Domain;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Domain.Bgv;

/// <summary>
/// The latest BGV state of one application, kept from <c>bgv.*</c> events so confirmation can be
/// gated without a call to Bgv. BGV usually clears before the offer is accepted, so the track exists
/// independently of the case. Events can arrive out of order: an older one never overwrites a newer
/// one, two events from the same instant keep the further state, and an adverse outcome is final.
/// </summary>
public sealed class BgvTrack : Entity, ITenantOwned
{
    private BgvTrack()
    {
    }

    public Guid TenantId { get; private set; }

    public string AppId { get; private set; } = string.Empty;

    public string? CaseId { get; private set; }

    public BgvStatus Status { get; private set; }

    /// <summary>When the event behind <see cref="Status"/> happened.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public static BgvTrack Start(string appId, string? caseId, BgvStatus status, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        return new BgvTrack { Id = Guid.CreateVersion7(), AppId = appId, CaseId = caseId, Status = status, UpdatedAt = at };
    }

    /// <summary>Applies a newer state. Returns false when the event was older or the outcome is already final.</summary>
    public bool Apply(BgvStatus status, string? caseId, DateTimeOffset at)
    {
        if (Status == BgvStatus.Adverse || at < UpdatedAt || (at == UpdatedAt && status < Status))
        {
            return false;
        }

        Status = status;
        CaseId = caseId ?? CaseId;
        UpdatedAt = at;
        return true;
    }
}
