using Microsoft.EntityFrameworkCore;
using Recuro.Pipeline.Application.Abstractions;
using Recuro.Pipeline.Domain.Applications;
using Recuro.Pipeline.Domain.Requisitions;
using ApplicationEntity = Recuro.Pipeline.Domain.Applications.Application;

namespace Recuro.Pipeline.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="PipelineDbContext"/> scopes every query.</summary>
internal sealed class ApplicationRepository(PipelineDbContext db) : IApplicationRepository
{
    private static readonly ApplicationStage[] Closed = [ApplicationStage.Rejected, ApplicationStage.Withdrawn, ApplicationStage.Confirmed];

    public Task<ApplicationEntity?> GetAsync(string appId, CancellationToken ct) =>
        db.Applications.FirstOrDefaultAsync(a => a.AppId == appId, ct);

    public async Task<IReadOnlyList<ApplicationEntity>> ListByRequisitionAsync(string reqId, CancellationToken ct) =>
        await db.Applications.AsNoTracking().AsSplitQuery()
            .Where(a => a.ReqId == reqId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ApplicationEntity>> ListOpenByRequisitionAsync(string reqId, CancellationToken ct) =>
        await db.Applications.Where(a => a.ReqId == reqId && !Closed.Contains(a.Stage)).ToListAsync(ct);

    public Task<ApplicationEntity?> FindOpenAsync(string reqId, string candidateId, CancellationToken ct) =>
        db.Applications.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ReqId == reqId && a.CandidateId == candidateId && !Closed.Contains(a.Stage), ct);

    public async Task<IReadOnlyList<ApplicationEntity>> ListTatCandidatesAsync(
        ApplicationStage stage,
        DateTimeOffset enteredBefore,
        int limit,
        CancellationToken ct) =>
        await db.Applications
            .Where(a => a.Stage == stage && a.TatBreachedAt == null && a.StageEnteredAt < enteredBefore)
            .OrderBy(a => a.StageEnteredAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<ApplicationStage, int>> CountByStageAsync(CancellationToken ct) =>
        await db.Applications.AsNoTracking()
            .GroupBy(a => a.Stage)
            .Select(g => new { Stage = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Stage, g => g.Count, ct);

    public async Task<IReadOnlyList<ApplicationEntity>> ListTatBreachedAsync(int limit, CancellationToken ct) =>
        await db.Applications.AsNoTracking()
            .Where(a => a.TatBreachedAt != null && a.Stage != ApplicationStage.Hold && !Closed.Contains(a.Stage))
            .OrderBy(a => a.StageEnteredAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<int> CountJoiningBetweenAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        db.Applications.CountAsync(
            a => a.Stage == ApplicationStage.PreBoarding && a.ExpectedJoiningDate >= from && a.ExpectedJoiningDate <= to,
            ct);

    public void Add(ApplicationEntity application) => db.Applications.Add(application);
}

internal sealed class SourcingGateRepository(PipelineDbContext db) : ISourcingGateRepository
{
    public Task<SourcingGate?> GetAsync(string reqId, CancellationToken ct) =>
        db.SourcingGates.FirstOrDefaultAsync(g => g.ReqId == reqId, ct);

    public void Add(SourcingGate gate) => db.SourcingGates.Add(gate);
}
