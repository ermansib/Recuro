using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Domain.Kpis;
using Recuro.Reporting.Domain.Periods;
using Recuro.Reporting.Domain.Projections;

namespace Recuro.Reporting.Application.Reports.Queries;

/// <summary>
/// RCU-RPT-001/002 (frontend story): the nine §12 metrics for a period, with targets, status chips and
/// definitions. A period that has a frozen snapshot returns it; otherwise the register is computed live.
/// No period means the current month in the tenant's time zone.
/// </summary>
public sealed record GetKpiRegisterQuery(string? Period) : IQuery<KpiRegisterDto>;

internal sealed class GetKpiRegisterQueryHandler(
    IKpiReportBuilder builder,
    ISnapshotStore snapshots,
    IReportCalendar calendar,
    IReportMask mask) : IQueryHandler<GetKpiRegisterQuery, KpiRegisterDto>
{
    public async Task<Result<KpiRegisterDto>> Handle(GetKpiRegisterQuery query, CancellationToken ct)
    {
        var period = await PeriodArgument.ResolveAsync(query.Period, calendar, ct);
        if (period is null)
        {
            return ReportingErrors.InvalidPeriod;
        }

        var register = await RegisterReader.ReadAsync(period, builder, snapshots, ct);
        return (await mask.ForCallerAsync(ct))(register);
    }
}

/// <summary>Source Mix &amp; Cost Effectiveness for a period (RCU-RPT-003); spend per channel is masked for HR-TA.</summary>
public sealed record GetSourceMixQuery(string? Period) : IQuery<SourceMixDto>;

internal sealed class GetSourceMixQueryHandler(
    IKpiReportBuilder builder,
    ISnapshotStore snapshots,
    IReportCalendar calendar,
    IReportMask mask) : IQueryHandler<GetSourceMixQuery, SourceMixDto>
{
    public async Task<Result<SourceMixDto>> Handle(GetSourceMixQuery query, CancellationToken ct)
    {
        var period = await PeriodArgument.ResolveAsync(query.Period, calendar, ct);
        if (period is null)
        {
            return ReportingErrors.InvalidPeriod;
        }

        var register = (await mask.ForCallerAsync(ct))(await RegisterReader.ReadAsync(period, builder, snapshots, ct));
        return new SourceMixDto(register.Period, register.PeriodLabel, register.Hires, register.Currency, register.SourceMix);
    }
}

/// <summary>The KPI card of the TA dashboard (RCU-DSH-001), for the gateway's <c>/bff/dashboard/ta</c>.</summary>
public sealed record GetTaDashboardQuery : IQuery<DashboardFragmentDto>;

internal sealed class GetTaDashboardQueryHandler(
    IKpiReportBuilder builder,
    IMetricDefinitionStore definitions,
    IReportCalendar calendar,
    IReportMask mask,
    TimeProvider clock) : IQueryHandler<GetTaDashboardQuery, DashboardFragmentDto>
{
    public async Task<Result<DashboardFragmentDto>> Handle(GetTaDashboardQuery query, CancellationToken ct)
    {
        var period = await calendar.CurrentAsync(Cadence.Monthly, ct);
        var register = (await mask.ForCallerAsync(ct))(await builder.BuildAsync(period, ct));
        var set = await definitions.ActiveAtAsync(clock.GetUtcNow(), ct);
        var onDashboard = (set?.Definitions ?? DefaultMetricDefinitions.All).Where(d => d.Dashboard).Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        var order = DefaultMetricDefinitions.DashboardOrder;
        var rows = register.Kpis
            .Where(k => onDashboard.Contains(k.Key))
            .OrderBy(k => order.Contains(k.Key) ? order.ToList().IndexOf(k.Key) : int.MaxValue)
            .Select(k => new DashboardKpiDto(k.Name, k.Target, k.Value, k.Status, k.Tone, k.Trend))
            .ToList();
        return new DashboardFragmentDto(rows);
    }
}

/// <summary>RCU-RPT-004: the six-stage funnel for applications created in a range (default: the last 90 days).</summary>
public sealed record GetFunnelQuery(DateTimeOffset? From, DateTimeOffset? To, int? Days) : IQuery<FunnelDto>;

internal sealed class GetFunnelQueryValidator : AbstractValidator<GetFunnelQuery>
{
    public GetFunnelQueryValidator()
    {
        RuleFor(q => q.Days).InclusiveBetween(1, ReportingLimits.MaxFunnelDays);
        RuleFor(q => q.To).GreaterThan(q => q.From).When(q => q.From is not null && q.To is not null);
    }
}

internal sealed class GetFunnelQueryHandler(IProjectionStore projections, TimeProvider clock) : IQueryHandler<GetFunnelQuery, FunnelDto>
{
    public async Task<Result<FunnelDto>> Handle(GetFunnelQuery query, CancellationToken ct)
    {
        var to = query.To ?? clock.GetUtcNow();
        var from = query.From ?? to.AddDays(-(query.Days ?? ReportingLimits.DefaultFunnelDays));
        var window = new ReportWindow(from, to);
        var data = await projections.LoadAsync(from, to, ct);
        var steps = Funnel.Count(data.Applications, window)
            .Select(s => new FunnelStepDto(ReportFormat.Stage(s.Stage), s.Count, s.ConversionPercent))
            .ToList();
        return new FunnelDto(ReportFormat.Instant(from), ReportFormat.Instant(to), steps);
    }
}

/// <summary>Reads a period's register: the newest frozen snapshot when one exists, else a live computation.</summary>
internal static class RegisterReader
{
    public static async Task<KpiRegisterDto> ReadAsync(ReportPeriod period, IKpiReportBuilder builder, ISnapshotStore snapshots, CancellationToken ct)
    {
        var snapshot = await snapshots.LatestAsync(period.Key, ct);
        if (snapshot is null)
        {
            return await builder.BuildAsync(period, ct);
        }

        return SnapshotPayload.Deserialize(snapshot.Payload) with
        {
            Snapshot = new SnapshotRefDto(snapshot.Id, snapshot.Revision, snapshot.Hash, ReportFormat.Instant(snapshot.ComputedAt)),
        };
    }
}

/// <summary>Parses the optional <c>period</c> argument; empty means the current month.</summary>
internal static class PeriodArgument
{
    public static async Task<ReportPeriod?> ResolveAsync(string? value, IReportCalendar calendar, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return await calendar.CurrentAsync(Cadence.Monthly, ct);
        }

        return ReportPeriod.TryParse(value, out var period) ? period : null;
    }
}

/// <summary>Snapshots (RCU-RPT-002/005), newest first, optionally for one period.</summary>
public sealed record ListSnapshotsQuery(string? Period, int? Limit) : IQuery<IReadOnlyList<SnapshotDto>>;

internal sealed class ListSnapshotsQueryValidator : AbstractValidator<ListSnapshotsQuery>
{
    public ListSnapshotsQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, ReportingLimits.MaxPageSize);
        RuleFor(q => q.Period).Must(p => p is null || ReportPeriod.TryParse(p, out _)).WithMessage("Use yyyy-MM or yyyy-Qn.");
    }
}

internal sealed class ListSnapshotsQueryHandler(ISnapshotStore snapshots) : IQueryHandler<ListSnapshotsQuery, IReadOnlyList<SnapshotDto>>
{
    public async Task<Result<IReadOnlyList<SnapshotDto>>> Handle(ListSnapshotsQuery query, CancellationToken ct)
    {
        var period = query.Period is { } p && ReportPeriod.TryParse(p, out var parsed) ? parsed.Key : null;
        var rows = await snapshots.ListAsync(period, query.Limit ?? ReportingLimits.DefaultPageSize, ct);
        return rows.Select(SnapshotDto.From).ToList();
    }
}

/// <summary>Archived packs the caller may open: HR Head sees all, anyone else only packs addressed to their role.</summary>
public sealed record ListPacksQuery(int? Limit) : IQuery<IReadOnlyList<PackDto>>;

internal sealed class ListPacksQueryHandler(IPackArchive packs, ICurrentUser caller) : IQueryHandler<ListPacksQuery, IReadOnlyList<PackDto>>
{
    public async Task<Result<IReadOnlyList<PackDto>>> Handle(ListPacksQuery query, CancellationToken ct) =>
        Result.Success(await packs.ListAsync(PackAccess.RolesFor(caller), Math.Clamp(query.Limit ?? ReportingLimits.DefaultPageSize, 1, ReportingLimits.MaxPageSize), ct));
}

/// <summary>One pack file. <see cref="Format"/> is <c>pdf</c> or <c>csv</c>.</summary>
public sealed record GetPackFileQuery(Guid PackId, string Format) : IQuery<PackFile>;

public sealed record PackFile(byte[] Content, string ContentType, string FileName);

internal sealed class GetPackFileQueryHandler(IPackArchive packs, ICurrentUser caller) : IQueryHandler<GetPackFileQuery, PackFile>
{
    public async Task<Result<PackFile>> Handle(GetPackFileQuery query, CancellationToken ct)
    {
        var pack = await packs.GetAsync(query.PackId, ct);
        if (pack is null)
        {
            return ReportingErrors.PackNotFound;
        }

        if (PackAccess.RolesFor(caller) is { } roles && !roles.Contains(pack.RecipientRole))
        {
            return ReportingErrors.PackForbidden;
        }

        var name = $"recruitment-kpis-{pack.Period}-{pack.RecipientRole}";
        return string.Equals(query.Format, "csv", StringComparison.OrdinalIgnoreCase)
            ? new PackFile(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(pack.Csv)).ToArray(), "text/csv; charset=utf-8", name + ".csv")
            : new PackFile(pack.Pdf, "application/pdf", name + ".pdf");
    }
}

/// <summary>HR Head reads every pack; other report readers only packs addressed to one of their roles.</summary>
internal static class PackAccess
{
    public const string HrHead = "hrhead";

    /// <summary>Null means no restriction.</summary>
    public static IReadOnlyCollection<string>? RolesFor(ICurrentUser caller) =>
        caller.IsInRole(HrHead) ? null : caller.Roles.ToList();
}
