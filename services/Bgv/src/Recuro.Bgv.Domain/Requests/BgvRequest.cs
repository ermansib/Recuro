using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Domain.Requests;

/// <summary>
/// An application that reached the BGV stage in the Pipeline service (<c>pipeline.stage.changed</c> to BGV).
/// It is this service's local copy of the fact, so initiation never calls Pipeline synchronously.
/// </summary>
public sealed class BgvRequest : Entity, ITenantOwned
{
    private BgvRequest()
    {
    }

    public Guid TenantId { get; private set; }

    public string AppId { get; private set; } = string.Empty;

    public string ReqId { get; private set; } = string.Empty;

    public string CandidateId { get; private set; } = string.Empty;

    /// <summary>Candidate source; <c>IJP</c> defaults the check scope to delta-only (RCU-PIP-004, PPL-008).</summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>True while the application sits at BGV (or on hold from it).</summary>
    public bool AtBgv { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsInternal => string.Equals(Source, "IJP", StringComparison.Ordinal);

    public static BgvRequest Arrived(string appId, string reqId, string candidateId, string source, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        return new BgvRequest
        {
            Id = Guid.CreateVersion7(),
            AppId = appId,
            ReqId = reqId,
            CandidateId = candidateId,
            Source = source,
            AtBgv = true,
            UpdatedAt = at,
        };
    }

    /// <summary>Events can arrive out of order: only a newer fact changes the record.</summary>
    public void Update(bool atBgv, DateTimeOffset at)
    {
        if (at < UpdatedAt)
        {
            return;
        }

        AtBgv = atBgv;
        UpdatedAt = at;
    }
}
