using FluentValidation;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Domain.Periods;
using Recuro.Reporting.Domain.Reports;

namespace Recuro.Reporting.Application.Reports.Commands;

/// <summary>
/// RCU-RPT-002/005: freeze a period's register as a new snapshot revision. The register is recomputed
/// from the projections (rebuild them first with a replay when events arrived late or a fix landed); the
/// snapshot's SHA-256 is stored and logged, so later tampering or drift is detectable.
/// </summary>
public sealed record RecomputeSnapshotCommand(string Period, string? Reason) : ICommand<SnapshotDto>;

internal sealed class RecomputeSnapshotCommandValidator : AbstractValidator<RecomputeSnapshotCommand>
{
    public RecomputeSnapshotCommandValidator()
    {
        RuleFor(c => c.Period).Must(p => ReportPeriod.TryParse(p, out _)).WithMessage("Use yyyy-MM or yyyy-Qn.");
        RuleFor(c => c.Reason).MaximumLength(ReportingLimits.ReasonLength);
    }
}

internal sealed class RecomputeSnapshotCommandHandler(ISnapshotFreezer freezer, IUnitOfWork unitOfWork) : ICommandHandler<RecomputeSnapshotCommand, SnapshotDto>
{
    public async Task<Result<SnapshotDto>> Handle(RecomputeSnapshotCommand command, CancellationToken ct)
    {
        if (!ReportPeriod.TryParse(command.Period, out var period))
        {
            return ReportingErrors.InvalidPeriod;
        }

        var frozen = await freezer.FreezeAsync(period, command.Reason, ct);
        if (frozen.IsFailure)
        {
            return frozen.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return SnapshotDto.From(frozen.Value.Snapshot);
    }
}

/// <summary>A newly frozen snapshot and the register it holds.</summary>
public sealed record FrozenRegister(KpiSnapshot Snapshot, KpiRegisterDto Register);

/// <summary>Computes and stores a new snapshot revision. Shared by recompute, pack generation and the schedule.</summary>
public interface ISnapshotFreezer
{
    Task<Result<FrozenRegister>> FreezeAsync(ReportPeriod period, string? reason, CancellationToken ct);
}

internal sealed partial class SnapshotFreezer(
    IKpiReportBuilder builder,
    ISnapshotStore snapshots,
    IReportCalendar calendar,
    ICurrentUser caller,
    ITenantContext tenant,
    TimeProvider clock,
    ILogger<SnapshotFreezer> logger) : ISnapshotFreezer
{
    public async Task<Result<FrozenRegister>> FreezeAsync(ReportPeriod period, string? reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (period.InZone(await calendar.ZoneAsync(ct)).From > now)
        {
            return ReportingErrors.FuturePeriod;
        }

        var register = await builder.BuildAsync(period, ct);
        var payload = SnapshotPayload.Serialize(register);
        var hash = SnapshotPayload.Hash(register);
        var revision = ((await snapshots.LatestAsync(period.Key, ct))?.Revision ?? 0) + 1;
        var snapshot = KpiSnapshot.Freeze(period, revision, register.DefinitionsVersion, register.UpToSequence, payload, hash, reason, caller.Name ?? caller.UserId ?? "system", now);
        snapshots.Add(snapshot);
        Frozen(logger, tenant.TenantId, period.Key, revision, hash, register.UpToSequence);
        return new FrozenRegister(snapshot, register);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "KPI snapshot for tenant {TenantId} period {Period} revision {Revision} frozen with hash {Hash} (log offset {UpToSequence})")]
    private static partial void Frozen(ILogger logger, Guid? tenantId, string period, int revision, string hash, long upToSequence);
}

/// <summary>
/// RCU-RPT-003: build the pack for a period and recipient role (default: HR Head monthly, MD/CEO
/// quarterly, per the tenant's settings), archive the PDF and CSV, and with <see cref="Send"/> hand it
/// to Notification to email. The register in the pack is masked for the recipient role.
/// </summary>
public sealed record GeneratePackCommand(string Period, string? RecipientRole, bool Send) : ICommand<PackDto>;

internal sealed class GeneratePackCommandValidator : AbstractValidator<GeneratePackCommand>
{
    public GeneratePackCommandValidator()
    {
        RuleFor(c => c.Period).Must(p => ReportPeriod.TryParse(p, out _)).WithMessage("Use yyyy-MM or yyyy-Qn.");
        RuleFor(c => c.RecipientRole).Must(r => r is null || ReportRoles.Readers.Contains(r)).WithMessage("Recipient must be hrta, hrhead or mdceo.");
    }
}

internal sealed class GeneratePackCommandHandler(IPackBuilder packs, IReportSettingsStore settings, IUnitOfWork unitOfWork) : ICommandHandler<GeneratePackCommand, PackDto>
{
    public async Task<Result<PackDto>> Handle(GeneratePackCommand command, CancellationToken ct)
    {
        if (!ReportPeriod.TryParse(command.Period, out var period))
        {
            return ReportingErrors.InvalidPeriod;
        }

        var role = command.RecipientRole ?? (await settings.GetAsync(ct))?.RecipientRole(period.Cadence) ?? ReportRoles.DefaultRecipient(period.Cadence);
        var pack = await packs.BuildAsync(period, role, command.Send, ct);
        if (pack.IsFailure)
        {
            return pack.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return PackDto.From(pack.Value);
    }
}

/// <summary>Builds, archives and optionally queues one pack. Reuses the period's newest snapshot when there is one.</summary>
public interface IPackBuilder
{
    Task<Result<ReportPack>> BuildAsync(ReportPeriod period, string recipientRole, bool send, CancellationToken ct);
}

internal sealed class PackBuilder(
    ISnapshotStore snapshots,
    ISnapshotFreezer freezer,
    IReportMask mask,
    IPackRenderer renderer,
    IPackArchive archive,
    ICurrentUser caller,
    TimeProvider clock) : IPackBuilder
{
    public async Task<Result<ReportPack>> BuildAsync(ReportPeriod period, string recipientRole, bool send, CancellationToken ct)
    {
        var snapshot = await snapshots.LatestAsync(period.Key, ct);
        KpiRegisterDto register;
        if (snapshot is null)
        {
            var frozen = await freezer.FreezeAsync(period, "pack", ct);
            if (frozen.IsFailure)
            {
                return frozen.Error!;
            }

            (snapshot, register) = (frozen.Value.Snapshot, frozen.Value.Register);
        }
        else
        {
            register = SnapshotPayload.Deserialize(snapshot.Payload);
        }

        var masked = (await mask.ForRoleAsync(recipientRole, ct))(register);
        var rendered = renderer.Render(masked, recipientRole);
        var pack = ReportPack.Archive(snapshot, recipientRole, rendered.Pdf, rendered.Csv, caller.Name ?? caller.UserId ?? "system", clock.GetUtcNow());
        archive.Add(pack);
        if (send)
        {
            archive.Queue(pack, new PackReadyPayload(
                pack.Id,
                period.Key,
                period.Label,
                period.Cadence.ToString(),
                recipientRole,
                masked.OnTrack,
                $"{PackDto.BasePath}/{pack.Id}/pdf",
                $"{PackDto.BasePath}/{pack.Id}/csv",
                snapshot.Hash));
        }

        return pack;
    }
}

/// <summary>
/// The scheduled run for one tenant (RCU-RPT-003): for each enabled cadence, the period that just closed
/// in the tenant's time zone gets a snapshot and a pack emailed to its recipient role, once.
/// </summary>
public sealed record RunScheduledPacksCommand : ICommand<int>;

internal sealed class RunScheduledPacksCommandHandler(
    IReportSettingsStore settings,
    IReportCalendar calendar,
    IPackArchive archive,
    IPackBuilder packs,
    IUnitOfWork unitOfWork) : ICommandHandler<RunScheduledPacksCommand, int>
{
    public async Task<Result<int>> Handle(RunScheduledPacksCommand command, CancellationToken ct)
    {
        var tenantSettings = await settings.GetAsync(ct);
        var sent = 0;
        foreach (var cadence in Enum.GetValues<Cadence>())
        {
            if (tenantSettings is not null && !tenantSettings.PackEnabled(cadence))
            {
                continue;
            }

            var closed = (await calendar.CurrentAsync(cadence, ct)).Previous();
            var role = tenantSettings?.RecipientRole(cadence) ?? ReportRoles.DefaultRecipient(cadence);
            if (await archive.ExistsAsync(closed.Key, cadence, role, ct))
            {
                continue;
            }

            var pack = await packs.BuildAsync(closed, role, send: true, ct);
            if (pack.IsSuccess)
            {
                await unitOfWork.SaveChangesAsync(ct);
                sent++;
            }
        }

        return sent;
    }
}

/// <summary>
/// RCU-RPT-001/005: re-fold the projections from the tenant's event log. <see cref="Reset"/> rebuilds from
/// scratch; otherwise events after <see cref="FromSequence"/> (default: the checkpoint) are re-applied,
/// which is safe because every fold is idempotent.
/// </summary>
public sealed record ReplayProjectionsCommand(long? FromSequence, bool Reset) : ICommand<ReplayResultDto>;

internal sealed class ReplayProjectionsCommandValidator : AbstractValidator<ReplayProjectionsCommand>
{
    public ReplayProjectionsCommandValidator() => RuleFor(c => c.FromSequence).GreaterThanOrEqualTo(0);
}

internal sealed partial class ReplayProjectionsCommandHandler(
    IProjectionStore projections,
    IEventLog log,
    Projections.ReportProjector projector,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    TimeProvider clock,
    ILogger<ReplayProjectionsCommandHandler> logger) : ICommandHandler<ReplayProjectionsCommand, ReplayResultDto>
{
    public async Task<Result<ReplayResultDto>> Handle(ReplayProjectionsCommand command, CancellationToken ct)
    {
        var result = await projections.ExclusiveAsync(
            async () =>
            {
                var checkpoint = await projections.CheckpointAsync(ct);
                if (command.Reset)
                {
                    await projections.ResetAsync(ct);
                    checkpoint.Reset(clock.GetUtcNow());
                }

                var from = command.Reset ? 0 : command.FromSequence ?? checkpoint.LastSequence;
                var after = from;
                var applied = 0;
                while (true)
                {
                    var batch = await log.ReadAfterAsync(after, ReportingLimits.ReplayBatch, ct);
                    if (batch.Count == 0)
                    {
                        break;
                    }

                    foreach (var entry in batch)
                    {
                        using var data = System.Text.Json.JsonDocument.Parse(entry.Data);
                        await projector.ApplyAsync(entry.Type, data.RootElement.Clone(), entry.OccurredAt, ct);
                        after = entry.Sequence;
                        applied++;
                    }

                    checkpoint.Advance(after, clock.GetUtcNow());
                    await unitOfWork.SaveChangesAsync(ct);
                }

                await unitOfWork.SaveChangesAsync(ct);
                return new ReplayResultDto(applied, from, after, command.Reset);
            },
            ct);
        Replayed(logger, tenant.TenantId, result.Applied, result.FromSequence, result.LastSequence, result.Reset);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Projections for tenant {TenantId} replayed: {Applied} events after offset {From} up to {Last} (reset: {Reset})")]
    private static partial void Replayed(ILogger logger, Guid? tenantId, int applied, long from, long last, bool reset);
}

/// <summary>RCU-RPT-003: HR Head records recruitment spend for a month and channel (CPH input).</summary>
public sealed record RecordCostCommand(string Month, string Source, decimal Amount, string Currency, string? Note) : ICommand<CostDto>;

internal sealed class RecordCostCommandValidator : AbstractValidator<RecordCostCommand>
{
    public RecordCostCommandValidator()
    {
        RuleFor(c => c.Month).Must(m => ReportPeriod.TryParse(m, out var p) && p.Cadence == Cadence.Monthly).WithMessage("Use yyyy-MM.");
        RuleFor(c => c.Source).NotEmpty().MaximumLength(ReportingLimits.SourceLength);
        RuleFor(c => c.Amount).GreaterThan(0).LessThan(ReportingLimits.MaxAmount);
        RuleFor(c => c.Currency).NotEmpty().Length(ReportingLimits.CurrencyLength).Matches("^[A-Za-z]{3}$");
        RuleFor(c => c.Note).MaximumLength(ReportingLimits.NoteLength);
    }
}

internal sealed class RecordCostCommandHandler(ICostLedger ledger, IUnitOfWork unitOfWork, ICurrentUser caller, TimeProvider clock) : ICommandHandler<RecordCostCommand, CostDto>
{
    public async Task<Result<CostDto>> Handle(RecordCostCommand command, CancellationToken ct)
    {
        if (!ReportPeriod.TryParse(command.Month, out var month))
        {
            return ReportingErrors.InvalidPeriod;
        }

        var cost = RecruitmentCost.Record(
            month.FirstDay,
            command.Source.Trim().ToLowerInvariant(),
            command.Amount,
            command.Currency,
            command.Note,
            caller.Name ?? caller.UserId ?? "unknown",
            clock.GetUtcNow());
        ledger.Add(cost);
        await unitOfWork.SaveChangesAsync(ct);
        return CostDto.From(cost);
    }
}

/// <summary>Who may receive or read reports (FRD §3.2 "View KPI reports").</summary>
public static class ReportRoles
{
    public static readonly IReadOnlySet<string> Readers = new HashSet<string>(StringComparer.Ordinal) { "hrta", "hrhead", "mdceo" };

    public static string DefaultRecipient(Cadence cadence) => cadence == Cadence.Monthly ? "hrhead" : "mdceo";
}
