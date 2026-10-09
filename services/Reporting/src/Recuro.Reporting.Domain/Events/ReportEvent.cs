using Recuro.BuildingBlocks.Domain;

namespace Recuro.Reporting.Domain.Events;

/// <summary>
/// One consumed integration event, kept as received (RCU-RPT-001). The log is Reporting's own event
/// history: projections are folded from it, and a replay or recompute (RCU-RPT-005) reads it again
/// instead of asking the services that published it. Rows are never changed. <see cref="Entity.Id"/>
/// is the CloudEvents id, so the same event is stored once.
/// </summary>
public sealed class ReportEvent : Entity, ITenantOwned
{
    private ReportEvent()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Position in the log, assigned by the database. Replays start after an offset.</summary>
    public long Sequence { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>The event's <c>data</c> as JSON. Payloads carry ids, never names or contact details.</summary>
    public string Data { get; private set; } = "{}";

    public static ReportEvent Record(Guid eventId, string type, string subject, DateTimeOffset occurredAt, DateTimeOffset receivedAt, string data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(data);
        return new ReportEvent
        {
            Id = eventId,
            Type = type,
            Subject = subject,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt,
            Data = data,
        };
    }
}

/// <summary>How far a projection has folded the tenant's log (RCU-RPT-001 checkpointed consumer).</summary>
public sealed class ProjectionCheckpoint : Entity, ITenantOwned
{
    private ProjectionCheckpoint()
    {
    }

    public Guid TenantId { get; private set; }

    public string Projection { get; private set; } = string.Empty;

    public long LastSequence { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ProjectionCheckpoint Start(string projection, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        Projection = projection,
        UpdatedAt = at,
    };

    /// <summary>Moves forward only; a replay that re-applies older events never moves it back.</summary>
    public void Advance(long sequence, DateTimeOffset at)
    {
        if (sequence > LastSequence)
        {
            LastSequence = sequence;
            UpdatedAt = at;
        }
    }

    /// <summary>A full rebuild starts again from the beginning of the log.</summary>
    public void Reset(DateTimeOffset at)
    {
        LastSequence = 0;
        UpdatedAt = at;
    }
}
