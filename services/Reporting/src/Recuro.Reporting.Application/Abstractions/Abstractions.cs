using Recuro.Reporting.Application.Reports;
using Recuro.Reporting.Domain.Events;
using Recuro.Reporting.Domain.Kpis;
using Recuro.Reporting.Domain.Periods;
using Recuro.Reporting.Domain.Projections;
using Recuro.Reporting.Domain.Reports;

namespace Recuro.Reporting.Application.Abstractions;

/// <summary>The tenant's log of consumed events (RCU-RPT-001). Append-only.</summary>
public interface IEventLog
{
    Task<bool> ContainsAsync(Guid eventId, CancellationToken ct);

    void Append(ReportEvent entry);

    /// <summary>Events after an offset, in log order.</summary>
    Task<IReadOnlyList<ReportEvent>> ReadAfterAsync(long afterSequence, int take, CancellationToken ct);

    Task<LogStatus> StatusAsync(CancellationToken ct);
}

/// <summary>How many events the tenant's log holds and the offset of the newest one.</summary>
public sealed record LogStatus(long Events, long LastSequence);

/// <summary>Everything the KPI formulas read for a time range.</summary>
public sealed record ReportingData(
    IReadOnlyList<ApplicationFact> Applications,
    IReadOnlyDictionary<string, RequisitionFact> Requisitions,
    IReadOnlyList<FeedbackFact> Feedback);

/// <summary>Read models folded from the log. Get-or-start methods return a tracked row.</summary>
public interface IProjectionStore
{
    Task<ApplicationFact> ApplicationAsync(string appId, CancellationToken ct);

    Task<RequisitionFact> RequisitionAsync(string reqId, CancellationToken ct);

    Task<FeedbackFact> FeedbackAsync(string interviewId, string interviewerId, string? appId, CancellationToken ct);

    Task<IReadOnlyList<ApplicationFact>> ApplicationsOfCandidateAsync(string candidateId, CancellationToken ct);

    Task<ProjectionCheckpoint> CheckpointAsync(CancellationToken ct);

    /// <summary>Deletes the tenant's projections before a full rebuild.</summary>
    Task ResetAsync(CancellationToken ct);

    /// <summary>
    /// Serialises projection writers for the tenant (live consumer and replays). Takes a transaction-scoped
    /// lock, so the caller must already be in a transaction (the event consumer is).
    /// </summary>
    Task LockAsync(CancellationToken ct);

    /// <summary>Runs <paramref name="work"/> in its own transaction holding the tenant's projection lock.</summary>
    Task<T> ExclusiveAsync<T>(Func<Task<T>> work, CancellationToken ct);

    /// <summary>Facts that matter to a range: applications created, hired or joined in it, their requisitions, feedback.</summary>
    Task<ReportingData> LoadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public interface ICostLedger
{
    void Add(RecruitmentCost cost);

    /// <summary>Spend for months in [<paramref name="fromMonth"/>, <paramref name="toMonthExclusive"/>).</summary>
    Task<IReadOnlyList<RecruitmentCost>> ListAsync(DateOnly fromMonth, DateOnly toMonthExclusive, CancellationToken ct);
}

public interface IMetricDefinitionStore
{
    /// <summary>The newest version in force at <paramref name="at"/>, or null when the tenant has none.</summary>
    Task<MetricDefinitionSet?> ActiveAtAsync(DateTimeOffset at, CancellationToken ct);

    Task<IReadOnlyList<MetricDefinitionSet>> ListAsync(CancellationToken ct);

    Task<int> LatestVersionAsync(CancellationToken ct);

    void Add(MetricDefinitionSet set);
}

public interface IReportSettingsStore
{
    Task<ReportSettings?> GetAsync(CancellationToken ct);

    void Add(ReportSettings settings);
}

public interface ISnapshotStore
{
    Task<KpiSnapshot?> LatestAsync(string period, CancellationToken ct);

    Task<IReadOnlyList<KpiSnapshot>> ListAsync(string? period, int limit, CancellationToken ct);

    void Add(KpiSnapshot snapshot);
}

/// <summary>Archived packs. <see cref="Queue"/> hands a pack to Notification through the outbox.</summary>
public interface IPackArchive
{
    void Add(ReportPack pack);

    Task<ReportPack?> GetAsync(Guid id, CancellationToken ct);

    Task<bool> ExistsAsync(string period, Cadence cadence, string recipientRole, CancellationToken ct);

    Task<IReadOnlyList<PackDto>> ListAsync(IReadOnlyCollection<string>? recipientRoles, int limit, CancellationToken ct);

    Task<ReportPack?> FindByReadyEventAsync(Guid readyEventId, CancellationToken ct);

    /// <summary>Publishes <c>reporting.pack.ready.v1</c> and marks the pack queued with that event's id.</summary>
    void Queue(ReportPack pack, PackReadyPayload payload);
}

/// <summary>Config's TAT and DOA matrices, as targets (architecture.md, "Config: matrices and versions").</summary>
public interface ITatTargetsSource
{
    /// <summary>Targets in force at <paramref name="at"/>. Falls back to the FRD values when Config is down.</summary>
    Task<TatTargets> GetAsync(DateTimeOffset at, CancellationToken ct);
}

/// <summary>Identity's masking maps for the <c>report</c> resource (RCU-AUT-004). Null means no map: fail closed.</summary>
public interface IMaskingMaps
{
    Task<IReadOnlyDictionary<string, string>?> GetAsync(string role, CancellationToken ct);
}

/// <summary>Builds a pack's PDF and CSV from a (masked) register.</summary>
public interface IPackRenderer
{
    RenderedPack Render(KpiRegisterDto register, string recipientRole);
}

public sealed record RenderedPack(byte[] Pdf, string Csv);

/// <summary>Defaults that apply before a tenant saves its own reporting settings.</summary>
public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    /// <summary>IANA time zone used for periods and schedules until the tenant picks one.</summary>
    public string DefaultTimeZone { get; set; } = "UTC";
}
