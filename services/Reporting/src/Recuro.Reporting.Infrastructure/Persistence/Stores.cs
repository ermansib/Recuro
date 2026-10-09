using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure.Locking;
using Recuro.BuildingBlocks.Infrastructure.Outbox;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Application.Events;
using Recuro.Reporting.Application.Reports;
using Recuro.Reporting.Domain.Events;
using Recuro.Reporting.Domain.Kpis;
using Recuro.Reporting.Domain.Periods;
using Recuro.Reporting.Domain.Projections;
using Recuro.Reporting.Domain.Reports;

namespace Recuro.Reporting.Infrastructure.Persistence;

/// <summary>Reporting's own event types, published through the outbox.</summary>
public static class ReportingEventTypes
{
    public const string PackReady = EventTypes.Reporting.PackReady;
}

internal sealed class EventLog(ReportingDbContext db) : IEventLog
{
    public Task<bool> ContainsAsync(Guid eventId, CancellationToken ct) => db.Events.AnyAsync(e => e.Id == eventId, ct);

    public void Append(ReportEvent entry) => db.Events.Add(entry);

    public async Task<IReadOnlyList<ReportEvent>> ReadAfterAsync(long afterSequence, int take, CancellationToken ct) =>
        await db.Events.AsNoTracking()
            .Where(e => e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .Take(take)
            .ToListAsync(ct);

    public async Task<LogStatus> StatusAsync(CancellationToken ct)
    {
        var count = await db.Events.LongCountAsync(ct);
        var last = count == 0 ? 0 : await db.Events.MaxAsync(e => e.Sequence, ct);
        return new LogStatus(count, last);
    }
}

/// <summary>Projections. Get-or-start looks at rows added earlier in the same unit of work first.</summary>
internal sealed class ProjectionStore(ReportingDbContext db, ITenantContext tenant, TimeProvider clock) : IProjectionStore
{
    public async Task<ApplicationFact> ApplicationAsync(string appId, CancellationToken ct)
    {
        var fact = db.Applications.Local.FirstOrDefault(a => a.AppId == appId)
            ?? await db.Applications.FirstOrDefaultAsync(a => a.AppId == appId, ct);
        if (fact is null)
        {
            fact = ApplicationFact.Start(appId);
            db.Applications.Add(fact);
        }

        return fact;
    }

    public async Task<RequisitionFact> RequisitionAsync(string reqId, CancellationToken ct)
    {
        var fact = db.Requisitions.Local.FirstOrDefault(r => r.ReqId == reqId)
            ?? await db.Requisitions.FirstOrDefaultAsync(r => r.ReqId == reqId, ct);
        if (fact is null)
        {
            fact = RequisitionFact.Start(reqId);
            db.Requisitions.Add(fact);
        }

        return fact;
    }

    public async Task<FeedbackFact> FeedbackAsync(string interviewId, string interviewerId, string? appId, CancellationToken ct)
    {
        var fact = db.Feedback.Local.FirstOrDefault(f => f.InterviewId == interviewId && f.InterviewerId == interviewerId)
            ?? await db.Feedback.FirstOrDefaultAsync(f => f.InterviewId == interviewId && f.InterviewerId == interviewerId, ct);
        if (fact is null)
        {
            fact = FeedbackFact.Start(interviewId, interviewerId, appId);
            db.Feedback.Add(fact);
        }

        return fact;
    }

    public async Task<IReadOnlyList<ApplicationFact>> ApplicationsOfCandidateAsync(string candidateId, CancellationToken ct) =>
        await db.Applications.Where(a => a.CandidateId == candidateId).ToListAsync(ct);

    public async Task<ProjectionCheckpoint> CheckpointAsync(CancellationToken ct)
    {
        var checkpoint = db.Checkpoints.Local.FirstOrDefault(c => c.Projection == ReportEventHandler.Projection)
            ?? await db.Checkpoints.FirstOrDefaultAsync(c => c.Projection == ReportEventHandler.Projection, ct);
        if (checkpoint is null)
        {
            checkpoint = ProjectionCheckpoint.Start(ReportEventHandler.Projection, clock.GetUtcNow());
            db.Checkpoints.Add(checkpoint);
        }

        return checkpoint;
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        // The tenant query filter scopes each bulk delete to the current tenant.
        await db.Applications.ExecuteDeleteAsync(ct);
        await db.Requisitions.ExecuteDeleteAsync(ct);
        await db.Feedback.ExecuteDeleteAsync(ct);
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is ApplicationFact or RequisitionFact or FeedbackFact).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    public Task LockAsync(CancellationToken ct) => PostgresLocks.LockAsync(db, LockName, ct);

    public async Task<T> ExclusiveAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(ct);
            var result = await work();
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    public async Task<ReportingData> LoadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var applications = await db.Applications.AsNoTracking()
            .Where(a => (a.CreatedAt >= from && a.CreatedAt < to)
                        || (a.OfferAcceptedAt >= from && a.OfferAcceptedAt < to)
                        || (a.JoinedAt >= from && a.JoinedAt < to))
            .ToListAsync(ct);
        var reqIds = applications.Where(a => a.ReqId is not null).Select(a => a.ReqId!).Distinct().ToList();
        var requisitions = await db.Requisitions.AsNoTracking()
            .Where(r => reqIds.Contains(r.ReqId) || (r.ApprovedAt >= from && r.ApprovedAt < to))
            .ToDictionaryAsync(r => r.ReqId, StringComparer.Ordinal, ct);
        var feedback = await db.Feedback.AsNoTracking()
            .Where(f => (f.SubmittedAt >= from && f.SubmittedAt < to) || (f.SubmittedAt == null && f.OverdueAt >= from && f.OverdueAt < to))
            .ToListAsync(ct);
        return new ReportingData(applications, requisitions, feedback);
    }

    private string LockName => $"reporting:projections:{tenant.RequiredTenantId}";
}

internal sealed class CostLedger(ReportingDbContext db) : ICostLedger
{
    public void Add(RecruitmentCost cost) => db.Costs.Add(cost);

    public async Task<IReadOnlyList<RecruitmentCost>> ListAsync(DateOnly fromMonth, DateOnly toMonthExclusive, CancellationToken ct) =>
        await db.Costs.AsNoTracking()
            .Where(c => c.Month >= fromMonth && c.Month < toMonthExclusive)
            .OrderBy(c => c.Month)
            .ThenBy(c => c.Source)
            .ToListAsync(ct);
}

internal sealed class MetricDefinitionStore(ReportingDbContext db) : IMetricDefinitionStore
{
    public Task<MetricDefinitionSet?> ActiveAtAsync(DateTimeOffset at, CancellationToken ct) =>
        db.DefinitionSets.AsNoTracking()
            .Where(s => s.EffectiveFrom <= at)
            .OrderByDescending(s => s.EffectiveFrom)
            .ThenByDescending(s => s.Version)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<MetricDefinitionSet>> ListAsync(CancellationToken ct) =>
        await db.DefinitionSets.AsNoTracking().OrderByDescending(s => s.Version).ToListAsync(ct);

    public async Task<int> LatestVersionAsync(CancellationToken ct) =>
        await db.DefinitionSets.MaxAsync(s => (int?)s.Version, ct) ?? DefaultMetricDefinitions.Version;

    public void Add(MetricDefinitionSet set) => db.DefinitionSets.Add(set);
}

internal sealed class ReportSettingsStore(ReportingDbContext db) : IReportSettingsStore
{
    public async Task<ReportSettings?> GetAsync(CancellationToken ct) =>
        db.Settings.Local.FirstOrDefault() ?? await db.Settings.FirstOrDefaultAsync(ct);

    public void Add(ReportSettings settings) => db.Settings.Add(settings);
}

internal sealed class SnapshotStore(ReportingDbContext db) : ISnapshotStore
{
    public async Task<KpiSnapshot?> LatestAsync(string period, CancellationToken ct) =>
        db.Snapshots.Local.Where(s => s.Period == period).MaxBy(s => s.Revision)
        ?? await db.Snapshots.AsNoTracking().Where(s => s.Period == period).OrderByDescending(s => s.Revision).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<KpiSnapshot>> ListAsync(string? period, int limit, CancellationToken ct)
    {
        var query = db.Snapshots.AsNoTracking();
        if (period is not null)
        {
            query = query.Where(s => s.Period == period);
        }

        return await query.OrderByDescending(s => s.ComputedAt).Take(limit).ToListAsync(ct);
    }

    public void Add(KpiSnapshot snapshot) => db.Snapshots.Add(snapshot);
}

internal sealed class PackArchive(ReportingDbContext db, IIntegrationEventPublisher publisher) : IPackArchive
{
    public void Add(ReportPack pack) => db.Packs.Add(pack);

    public Task<ReportPack?> GetAsync(Guid id, CancellationToken ct) => db.Packs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> ExistsAsync(string period, Cadence cadence, string recipientRole, CancellationToken ct) =>
        db.Packs.AnyAsync(p => p.Period == period && p.Cadence == cadence && p.RecipientRole == recipientRole, ct);

    public async Task<IReadOnlyList<PackDto>> ListAsync(IReadOnlyCollection<string>? recipientRoles, int limit, CancellationToken ct)
    {
        var query = db.Packs.AsNoTracking();
        if (recipientRoles is not null)
        {
            query = query.Where(p => recipientRoles.Contains(p.RecipientRole));
        }

        // Project without the files: a list never loads PDFs.
        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .Select(p => new { p.Id, p.Period, p.Cadence, p.RecipientRole, p.SnapshotId, p.SnapshotHash, p.Delivery, p.DeliveredAt, p.CreatedBy, p.CreatedAt })
            .ToListAsync(ct);
        return rows.Select(p => new PackDto(
            p.Id,
            p.Period,
            p.Cadence.ToString(),
            p.RecipientRole,
            p.SnapshotId,
            p.SnapshotHash,
            p.Delivery.ToString(),
            p.DeliveredAt is { } at ? ReportFormat.Instant(at) : null,
            p.CreatedBy,
            ReportFormat.Instant(p.CreatedAt),
            $"{PackDto.BasePath}/{p.Id}/pdf",
            $"{PackDto.BasePath}/{p.Id}/csv")).ToList();
    }

    public Task<ReportPack?> FindByReadyEventAsync(Guid readyEventId, CancellationToken ct) =>
        db.Packs.FirstOrDefaultAsync(p => p.ReadyEventId == readyEventId, ct);

    public void Queue(ReportPack pack, PackReadyPayload payload)
    {
        ArgumentNullException.ThrowIfNull(pack);
        publisher.Publish(ReportingEventTypes.PackReady, $"ReportPack/{pack.Id}", payload);
        // The publisher stages the CloudEvent in the outbox; its id is what Notification will quote back.
        var staged = db.ChangeTracker.Entries<OutboxMessage>()
            .Where(e => e.State == EntityState.Added && e.Entity.Type == ReportingEventTypes.PackReady)
            .Select(e => e.Entity)
            .Last();
        pack.Queued(staged.Id);
    }
}
