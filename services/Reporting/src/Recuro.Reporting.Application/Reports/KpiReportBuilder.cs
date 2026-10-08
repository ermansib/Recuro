using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Domain.Kpis;
using Recuro.Reporting.Domain.Periods;

namespace Recuro.Reporting.Application.Reports;

/// <summary>The tenant's reporting time zone and "now" in it (RCU-RPT-003 "tenant-tz").</summary>
public interface IReportCalendar
{
    Task<TimeZoneInfo> ZoneAsync(CancellationToken ct);

    /// <summary>The period of <paramref name="cadence"/> that contains today in the tenant's zone.</summary>
    Task<ReportPeriod> CurrentAsync(Cadence cadence, CancellationToken ct);
}

internal sealed class ReportCalendar(IReportSettingsStore settings, ReportingOptions options, TimeProvider clock) : IReportCalendar
{
    public async Task<TimeZoneInfo> ZoneAsync(CancellationToken ct)
    {
        var id = (await settings.GetAsync(ct))?.TimeZone ?? options.DefaultTimeZone;
        return TimeZones.Find(id) ?? TimeZoneInfo.Utc;
    }

    public async Task<ReportPeriod> CurrentAsync(Cadence cadence, CancellationToken ct)
    {
        var zone = await ZoneAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        return ReportPeriod.Containing(cadence, today);
    }
}

public static class TimeZones
{
    public static TimeZoneInfo? Find(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null;
}

/// <summary>Computes the §12 register for a period from the projections (unmasked).</summary>
public interface IKpiReportBuilder
{
    Task<KpiRegisterDto> BuildAsync(ReportPeriod period, CancellationToken ct);
}

/// <summary>
/// RCU-RPT-002: every metric is computed by its formula from the registry's definition in force for the
/// period, with TAT targets from Config, plus a trailing trend of the same cadence.
/// </summary>
internal sealed class KpiReportBuilder(
    IProjectionStore projections,
    ICostLedger costs,
    IMetricDefinitionStore definitions,
    ITatTargetsSource targets,
    IEventLog log,
    IReportCalendar calendar,
    TimeProvider clock) : IKpiReportBuilder
{
    public const int TrendLength = 7;

    public async Task<KpiRegisterDto> BuildAsync(ReportPeriod period, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(period);
        var zone = await calendar.ZoneAsync(ct);
        var now = clock.GetUtcNow();
        var window = period.InZone(zone);
        var status = await log.StatusAsync(ct);

        var set = await definitions.ActiveAtAsync(window.To < now ? window.To : now, ct);
        var metrics = set?.Definitions ?? DefaultMetricDefinitions.All;
        var tat = await targets.GetAsync(window.To < now ? window.To : now, ct);

        var trailing = period.Trailing(TrendLength);
        var first = trailing[0].InZone(zone);
        var data = await projections.LoadAsync(first.From, window.To, ct);
        var spend = await costs.ListAsync(trailing[0].FirstDay, period.EndExclusive, ct);

        var inputsByPeriod = trailing.ToDictionary(
            p => p.Key,
            p => new KpiInputs(p, p.InZone(zone), now, data.Applications, data.Requisitions, data.Feedback, spend, tat),
            StringComparer.Ordinal);
        var current = inputsByPeriod[period.Key];

        var kpis = new List<KpiDto>();
        var onTrack = 0;
        var scored = 0;
        KpiMeasurement? mix = null;
        foreach (var definition in metrics)
        {
            var measurement = KpiFormulas.Measure(current, definition);
            var kpiStatus = KpiFormulas.Evaluate(definition, measurement);
            if (definition.Key == MetricKeys.SourceMix)
            {
                mix = measurement;
            }

            if (kpiStatus is KpiStatus.OnTrack or KpiStatus.Review && measurement.Sample > 0)
            {
                onTrack++;
            }

            scored++;
            var (label, tone) = ReportFormat.Status(kpiStatus);
            var trend = trailing
                .Select(p => (double)(KpiFormulas.Measure(inputsByPeriod[p.Key], definition).Actual ?? 0m))
                .ToList();
            kpis.Add(new KpiDto(
                definition.Key,
                definition.Name,
                definition.TargetLabel,
                ReportFormat.Value(definition, measurement),
                label,
                tone,
                trend,
                definition.Description,
                ReportFormat.Unit(definition.Unit),
                measurement.Actual,
                measurement.Target,
                measurement.Sample,
                measurement.Currency));
        }

        var sourceMix = (mix ?? KpiFormulas.Measure(current, DefaultMetricDefinitions.All.Single(d => d.Key == MetricKeys.SourceMix))).Mix ?? [];
        return new KpiRegisterDto(
            period.Key,
            period.Label,
            period.Cadence.ToString(),
            ReportFormat.Instant(window.From),
            ReportFormat.Instant(window.To),
            set?.Version ?? DefaultMetricDefinitions.Version,
            tat.ConfigVersion,
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{onTrack} / {scored}"),
            kpis,
            current.Hires.Count,
            mix?.Currency,
            sourceMix.Select(s => new MixShareDto(s.Source, s.Hires, s.Percent, s.Cost, s.CostPerHire)).ToList(),
            status.LastSequence,
            ReportFormat.Instant(now),
            null);
    }
}

/// <summary>Canonical JSON and SHA-256 for frozen registers (RCU-RPT-005 "snapshot hash logged").</summary>
public static class SnapshotPayload
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(KpiRegisterDto register) => JsonSerializer.Serialize(register with { Snapshot = null }, Options);

    public static KpiRegisterDto Deserialize(string payload) =>
        JsonSerializer.Deserialize<KpiRegisterDto>(payload, Options) ?? throw new JsonException("Empty snapshot payload.");

    /// <summary>
    /// The audit hash of a register's figures (RCU-RPT-005). It leaves out when it was computed, so a
    /// recompute over the same events gives the same hash and a changed hash means changed figures.
    /// </summary>
    public static string Hash(KpiRegisterDto register)
    {
        ArgumentNullException.ThrowIfNull(register);
        return Hash(Serialize(register with { ComputedAt = string.Empty }));
    }

    public static string Hash(string payload) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
