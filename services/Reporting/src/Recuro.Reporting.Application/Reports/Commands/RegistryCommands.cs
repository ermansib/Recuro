using System.Globalization;
using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Domain.Kpis;
using Recuro.Reporting.Domain.Periods;
using Recuro.Reporting.Domain.Reports;

namespace Recuro.Reporting.Application.Reports.Commands;

/// <summary>
/// RCU-RPT-002: HR Head publishes a new version of the metric-definition registry. It applies from
/// <see cref="EffectiveFrom"/> (default now) and never rewrites the past: frozen snapshots keep the
/// version they were computed with.
/// </summary>
public sealed record PublishDefinitionsCommand(DateTimeOffset? EffectiveFrom, IReadOnlyList<MetricDefinitionInput> Definitions) : ICommand<DefinitionsDto>;

/// <summary>One definition as sent by the client; enums are their names (<c>Days</c>, <c>AtLeast</c>, ...).</summary>
public sealed record MetricDefinitionInput(
    string Key,
    string Name,
    string Description,
    string Unit,
    string Direction,
    string TargetLabel,
    decimal? Target,
    string? TargetSource,
    decimal? TargetFactor,
    decimal? NearPercent,
    bool Dashboard);

internal sealed class PublishDefinitionsCommandValidator : AbstractValidator<PublishDefinitionsCommand>
{
    public PublishDefinitionsCommandValidator(TimeProvider clock)
    {
        RuleFor(c => c.EffectiveFrom)
            .Must(at => at is null || at >= clock.GetUtcNow().AddMinutes(-5))
            .WithMessage("effectiveFrom cannot be in the past; frozen periods keep their definitions.");
        RuleFor(c => c.Definitions).NotEmpty();
        RuleFor(c => c.Definitions)
            .Must(d => d is null || d.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == d.Count)
            .WithMessage("Each metric key may appear once.");
        RuleForEach(c => c.Definitions).ChildRules(d =>
        {
            d.RuleFor(x => x.Key).Must(KpiFormulas.IsKnown).WithMessage("Unknown metric key; use one of the nine §12 keys.");
            d.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            d.RuleFor(x => x.Description).NotEmpty().MaximumLength(ReportingLimits.NoteLength);
            d.RuleFor(x => x.TargetLabel).NotEmpty().MaximumLength(100);
            d.RuleFor(x => x.Unit).Must(u => Enum.TryParse<MetricUnit>(u, ignoreCase: true, out _)).WithMessage("Unit is Days, Percent, Money or Mix.");
            d.RuleFor(x => x.Direction).Must(v => Enum.TryParse<TargetDirection>(v, ignoreCase: true, out _)).WithMessage("Direction is AtMost, AtLeast or Review.");
            d.RuleFor(x => x.TargetSource)
                .Must(s => s is null || s == MetricKeys.OverallTatSource || (s.StartsWith(MetricKeys.TatStagePrefix, StringComparison.Ordinal) && s.Length > MetricKeys.TatStagePrefix.Length))
                .WithMessage("targetSource is doa:overall or tat:<stage>.");
            d.RuleFor(x => x.TargetFactor).InclusiveBetween(0.1m, 10m);
            d.RuleFor(x => x.NearPercent).InclusiveBetween(0m, 100m);
            d.RuleFor(x => x)
                .Must(x => !string.Equals(x.Direction, nameof(TargetDirection.Review), StringComparison.OrdinalIgnoreCase) ? x.Target is not null || x.TargetSource is not null : true)
                .WithName("target")
                .WithMessage("A pass/fail metric needs a target or a targetSource.");
        });
    }
}

internal sealed class PublishDefinitionsCommandHandler(
    IMetricDefinitionStore store,
    IUnitOfWork unitOfWork,
    ICurrentUser caller,
    TimeProvider clock) : ICommandHandler<PublishDefinitionsCommand, DefinitionsDto>
{
    public async Task<Result<DefinitionsDto>> Handle(PublishDefinitionsCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var definitions = command.Definitions.Select(d => new MetricDefinition(
            d.Key,
            d.Name.Trim(),
            d.Description.Trim(),
            Enum.Parse<MetricUnit>(d.Unit, ignoreCase: true),
            Enum.Parse<TargetDirection>(d.Direction, ignoreCase: true),
            d.TargetLabel.Trim(),
            d.Target,
            d.TargetSource,
            d.TargetFactor ?? 1m,
            d.NearPercent ?? 10m,
            d.Dashboard)).ToList();
        var version = await store.LatestVersionAsync(ct) + 1;
        store.Add(MetricDefinitionSet.Publish(version, command.EffectiveFrom ?? now, definitions, caller.Name ?? caller.UserId ?? "unknown", now));
        await unitOfWork.SaveChangesAsync(ct);
        return await DefinitionsReader.ReadAsync(store, now, ct);
    }
}

/// <summary>The definitions in force now and the version history.</summary>
public sealed record GetDefinitionsQuery : IQuery<DefinitionsDto>;

internal sealed class GetDefinitionsQueryHandler(IMetricDefinitionStore store, TimeProvider clock) : IQueryHandler<GetDefinitionsQuery, DefinitionsDto>
{
    public async Task<Result<DefinitionsDto>> Handle(GetDefinitionsQuery query, CancellationToken ct) =>
        await DefinitionsReader.ReadAsync(store, clock.GetUtcNow(), ct);
}

internal static class DefinitionsReader
{
    public static async Task<DefinitionsDto> ReadAsync(IMetricDefinitionStore store, DateTimeOffset now, CancellationToken ct)
    {
        var active = await store.ActiveAtAsync(now, ct);
        var versions = await store.ListAsync(ct);
        return new DefinitionsDto(
            active?.Version ?? DefaultMetricDefinitions.Version,
            active is null ? null : ReportFormat.Instant(active.EffectiveFrom),
            (active?.Definitions ?? DefaultMetricDefinitions.All).Select(MetricDefinitionDto.From).ToList(),
            versions.Select(v => new DefinitionVersionDto(v.Version, ReportFormat.Instant(v.EffectiveFrom), v.CreatedBy, ReportFormat.Instant(v.CreatedAt))).ToList());
    }
}

/// <summary>Recorded spend, optionally for one month or quarter.</summary>
public sealed record ListCostsQuery(string? Period) : IQuery<IReadOnlyList<CostDto>>;

internal sealed class ListCostsQueryValidator : AbstractValidator<ListCostsQuery>
{
    public ListCostsQueryValidator() =>
        RuleFor(q => q.Period).Must(p => p is null || ReportPeriod.TryParse(p, out _)).WithMessage("Use yyyy-MM or yyyy-Qn.");
}

internal sealed class ListCostsQueryHandler(ICostLedger ledger) : IQueryHandler<ListCostsQuery, IReadOnlyList<CostDto>>
{
    public async Task<Result<IReadOnlyList<CostDto>>> Handle(ListCostsQuery query, CancellationToken ct)
    {
        var (from, to) = query.Period is { } p && ReportPeriod.TryParse(p, out var period)
            ? (period.FirstDay, period.EndExclusive)
            : (DateOnly.MinValue, DateOnly.MaxValue);
        var rows = await ledger.ListAsync(from, to, ct);
        return rows.Select(CostDto.From).ToList();
    }
}

/// <summary>The tenant's reporting settings (defaults until saved).</summary>
public sealed record GetSettingsQuery : IQuery<SettingsDto>;

internal sealed class GetSettingsQueryHandler(IReportSettingsStore store, ReportingOptions options) : IQueryHandler<GetSettingsQuery, SettingsDto>
{
    public async Task<Result<SettingsDto>> Handle(GetSettingsQuery query, CancellationToken ct) =>
        SettingsDto.From(await store.GetAsync(ct) ?? ReportSettings.Create(options.DefaultTimeZone, DateTimeOffset.UnixEpoch));
}

/// <summary>HR Head sets the reporting time zone and which scheduled packs go to which role.</summary>
public sealed record UpdateSettingsCommand(string TimeZone, bool MonthlyPack, string MonthlyRecipientRole, bool QuarterlyPack, string QuarterlyRecipientRole) : ICommand<SettingsDto>;

internal sealed class UpdateSettingsCommandValidator : AbstractValidator<UpdateSettingsCommand>
{
    public UpdateSettingsCommandValidator()
    {
        RuleFor(c => c.TimeZone).NotEmpty().MaximumLength(ReportingLimits.TimeZoneLength)
            .Must(z => TimeZones.Find(z) is not null).WithMessage("Unknown time zone; use an IANA id such as Asia/Kolkata.");
        RuleFor(c => c.MonthlyRecipientRole).Must(ReportRoles.Readers.Contains).WithMessage("Recipient must be hrta, hrhead or mdceo.");
        RuleFor(c => c.QuarterlyRecipientRole).Must(ReportRoles.Readers.Contains).WithMessage("Recipient must be hrta, hrhead or mdceo.");
    }
}

internal sealed class UpdateSettingsCommandHandler(IReportSettingsStore store, IUnitOfWork unitOfWork, ICurrentUser caller, TimeProvider clock) : ICommandHandler<UpdateSettingsCommand, SettingsDto>
{
    public async Task<Result<SettingsDto>> Handle(UpdateSettingsCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var settings = await store.GetAsync(ct);
        if (settings is null)
        {
            settings = ReportSettings.Create(command.TimeZone, now);
            store.Add(settings);
        }

        settings.Update(command.TimeZone, command.MonthlyPack, command.MonthlyRecipientRole, command.QuarterlyPack, command.QuarterlyRecipientRole, caller.Name ?? caller.UserId ?? "unknown", now);
        await unitOfWork.SaveChangesAsync(ct);
        return SettingsDto.From(settings);
    }
}

/// <summary>How far the projections have folded the log (RCU-RPT-001 operations view).</summary>
public sealed record GetProjectionStatusQuery : IQuery<ProjectionStatusDto>;

internal sealed class GetProjectionStatusQueryHandler(IEventLog log, IProjectionStore projections) : IQueryHandler<GetProjectionStatusQuery, ProjectionStatusDto>
{
    public async Task<Result<ProjectionStatusDto>> Handle(GetProjectionStatusQuery query, CancellationToken ct)
    {
        var status = await log.StatusAsync(ct);
        var checkpoint = await projections.CheckpointAsync(ct);
        return new ProjectionStatusDto(
            status.Events,
            status.LastSequence,
            checkpoint.LastSequence,
            checkpoint.LastSequence == 0 ? null : checkpoint.UpdatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
    }
}
