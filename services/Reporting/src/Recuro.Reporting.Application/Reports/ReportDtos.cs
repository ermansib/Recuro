using System.Globalization;
using System.Text.Json.Serialization;
using Recuro.Reporting.Domain.Kpis;
using Recuro.Reporting.Domain.Reports;

namespace Recuro.Reporting.Application.Reports;

/// <summary>
/// One row of the §12 metric register. The first six fields are the frontend's dashboard KPI row
/// (<c>DashboardData.kpis</c>: name, target, value, status, tone, trend); the rest carry the numbers
/// behind it and the definition tooltip (RCU-RPT-001 frontend story).
/// </summary>
public sealed record KpiDto(
    string Key,
    string Name,
    string Target,
    string Value,
    string Status,
    string Tone,
    IReadOnlyList<double> Trend,
    string Definition,
    string Unit,
    decimal? Actual,
    decimal? TargetValue,
    int Sample,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Currency);

/// <summary>A source channel's share of hires and, for roles allowed to see it, spend and CPH (RCU-RPT-003).</summary>
public sealed record MixShareDto(
    string Source,
    int Hires,
    decimal Percent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Cost,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? CostPerHire);

/// <summary>Which frozen snapshot a register came from (RCU-RPT-002/005).</summary>
public sealed record SnapshotRefDto(Guid Id, int Revision, string Hash, string ComputedAt);

/// <summary>The full metric register for one period (prototype S-15 "Full Metric Register").</summary>
public sealed record KpiRegisterDto(
    string Period,
    string PeriodLabel,
    string Cadence,
    string From,
    string To,
    int DefinitionsVersion,
    string? ConfigVersion,
    string OnTrack,
    IReadOnlyList<KpiDto> Kpis,
    int Hires,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Currency,
    IReadOnlyList<MixShareDto> SourceMix,
    long UpToSequence,
    string ComputedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SnapshotRefDto? Snapshot);

/// <summary>Source Mix &amp; Cost Effectiveness card (RCU-RPT-003).</summary>
public sealed record SourceMixDto(
    string Period,
    string PeriodLabel,
    int Hires,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Currency,
    IReadOnlyList<MixShareDto> Sources);

/// <summary>A dashboard KPI row exactly as the frontend's <c>DashboardData.kpis</c> item.</summary>
public sealed record DashboardKpiDto(string Name, string Target, string Value, string Status, string Tone, IReadOnlyList<double> Trend);

/// <summary>This service's part of the TA dashboard (RCU-DSH-001), merged by the gateway's <c>/bff/dashboard/ta</c>.</summary>
public sealed record DashboardFragmentDto(IReadOnlyList<DashboardKpiDto> Kpis);

public sealed record FunnelStepDto(string Stage, int Count, decimal? Conversion);

/// <summary>Recruitment funnel (RCU-RPT-004): applications created in the range and how far they got.</summary>
public sealed record FunnelDto(string From, string To, IReadOnlyList<FunnelStepDto> Stages);

public sealed record SnapshotDto(
    Guid Id,
    string Period,
    string Cadence,
    int Revision,
    int DefinitionsVersion,
    long UpToSequence,
    string Hash,
    string? Reason,
    string ComputedBy,
    string ComputedAt)
{
    public static SnapshotDto From(KpiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new SnapshotDto(
            snapshot.Id,
            snapshot.Period,
            snapshot.Cadence.ToString(),
            snapshot.Revision,
            snapshot.DefinitionsVersion,
            snapshot.UpToSequence,
            snapshot.Hash,
            snapshot.Reason,
            snapshot.ComputedBy,
            ReportFormat.Instant(snapshot.ComputedAt));
    }
}

/// <summary>An archived pack without its files.</summary>
public sealed record PackDto(
    Guid Id,
    string Period,
    string Cadence,
    string RecipientRole,
    Guid SnapshotId,
    string SnapshotHash,
    string Delivery,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeliveredAt,
    string CreatedBy,
    string CreatedAt,
    string PdfUrl,
    string CsvUrl)
{
    public const string BasePath = "/api/v1/reports/packs";

    public static PackDto From(ReportPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return new PackDto(
            pack.Id,
            pack.Period,
            pack.Cadence.ToString(),
            pack.RecipientRole,
            pack.SnapshotId,
            pack.SnapshotHash,
            pack.Delivery.ToString(),
            pack.DeliveredAt is { } at ? ReportFormat.Instant(at) : null,
            pack.CreatedBy,
            ReportFormat.Instant(pack.CreatedAt),
            $"{BasePath}/{pack.Id}/pdf",
            $"{BasePath}/{pack.Id}/csv");
    }
}

/// <summary>
/// Data of <c>reporting.pack.ready.v1</c>: a pack is archived and should be emailed to everyone holding
/// <see cref="RecipientRole"/> (RCU-RPT-003, dispatched by Notification). Links are relative to the gateway.
/// </summary>
public sealed record PackReadyPayload(
    Guid PackId,
    string Period,
    string PeriodLabel,
    string Cadence,
    string RecipientRole,
    string OnTrack,
    string PdfPath,
    string CsvPath,
    string SnapshotHash);

public sealed record CostDto(Guid Id, string Month, string Source, decimal Amount, string Currency, string? Note, string RecordedBy, string RecordedAt)
{
    public static CostDto From(RecruitmentCost cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        return new CostDto(
            cost.Id,
            cost.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            cost.Source,
            cost.Amount,
            cost.Currency,
            cost.Note,
            cost.RecordedBy,
            ReportFormat.Instant(cost.RecordedAt));
    }
}

public sealed record MetricDefinitionDto(
    string Key,
    string Name,
    string Description,
    string Unit,
    string Direction,
    string TargetLabel,
    decimal? Target,
    string? TargetSource,
    decimal TargetFactor,
    decimal NearPercent,
    bool Dashboard)
{
    public static MetricDefinitionDto From(MetricDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new MetricDefinitionDto(d.Key, d.Name, d.Description, d.Unit.ToString(), d.Direction.ToString(), d.TargetLabel, d.Target, d.TargetSource, d.TargetFactor, d.NearPercent, d.Dashboard);
    }
}

public sealed record DefinitionVersionDto(int Version, string EffectiveFrom, string CreatedBy, string CreatedAt);

/// <summary>The definitions in force now, and every published version.</summary>
public sealed record DefinitionsDto(int Version, string? EffectiveFrom, IReadOnlyList<MetricDefinitionDto> Definitions, IReadOnlyList<DefinitionVersionDto> Versions);

public sealed record SettingsDto(string TimeZone, bool MonthlyPack, string MonthlyRecipientRole, bool QuarterlyPack, string QuarterlyRecipientRole)
{
    public static SettingsDto From(ReportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SettingsDto(settings.TimeZone, settings.MonthlyPack, settings.MonthlyRecipientRole, settings.QuarterlyPack, settings.QuarterlyRecipientRole);
    }
}

public sealed record ProjectionStatusDto(long Events, long LastSequence, long Checkpoint, string? CheckpointAt);

public sealed record ReplayResultDto(int Applied, long FromSequence, long LastSequence, bool Reset);
