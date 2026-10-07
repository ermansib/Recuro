using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Locking;
using Recuro.Config.Application.Abstractions;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="ConfigDbContext"/> scopes every query.</summary>
internal sealed class RuleSetVersions(ConfigDbContext db, ITenantContext tenant) : IRuleSetVersions
{
    public Task<RuleSetVersion?> GetAsync(Guid id, CancellationToken ct) =>
        db.Versions.FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<IReadOnlyList<RuleSetVersion>> ListAsync(MatrixType type, CancellationToken ct) =>
        await db.Versions.AsNoTracking()
            .Where(v => v.MatrixType == type)
            .OrderByDescending(v => v.Number)
            .ToListAsync(ct);

    public Task<RuleSetVersion?> FindActiveAtAsync(MatrixType type, DateTimeOffset at, CancellationToken ct) =>
        db.Versions.AsNoTracking()
            .Where(v => v.MatrixType == type && v.Status == VersionStatus.Active && v.EffectiveFrom <= at)
            .OrderByDescending(v => v.EffectiveFrom)
            .ThenByDescending(v => v.Number)
            .FirstOrDefaultAsync(ct);

    public async Task<RuleSetVersion?> AddNextAsync(MatrixType type, Func<int, RuleSetVersion?> create, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(create);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Numbers are 1, 2, 3… per tenant and type: take turns so two proposals never share one.
            await PostgresLocks.LockAsync(db, $"config-version:{tenant.RequiredTenantId}:{type}", ct);
            var last = await db.Versions.Where(v => v.MatrixType == type).MaxAsync(v => (int?)v.Number, ct) ?? 0;
            var version = create(last + 1);
            if (version is not null)
            {
                db.Versions.Add(version);
                await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return version;
        });
    }

    public async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
