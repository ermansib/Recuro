using Microsoft.EntityFrameworkCore;
using Recuro.Audit.Application.Abstractions;
using Recuro.Audit.Application.Entries;
using Recuro.Audit.Domain.Entries;

namespace Recuro.Audit.Infrastructure.Persistence;

/// <summary>Read side. The tenant query filter on <see cref="AuditDbContext"/> scopes every query.</summary>
internal sealed class AuditReadStore(AuditDbContext db) : IAuditReadStore
{
    public async Task<IReadOnlyList<AuditEventDto>> ListAsync(AuditFilter filter, CancellationToken ct)
    {
        var query = db.Entries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Entity))
        {
            query = query.Where(e => e.Entity == filter.Entity);
        }

        if (!string.IsNullOrWhiteSpace(filter.ActorId))
        {
            query = query.Where(e => e.ActorId == filter.ActorId);
        }

        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
        {
            query = query.Where(e => e.CorrelationId == filter.CorrelationId);
        }

        if (filter.From is { } from)
        {
            query = query.Where(e => e.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(e => e.OccurredAt <= to);
        }

        var entries = await query
            .OrderByDescending(e => e.Sequence)
            .Take(filter.Limit)
            .ToListAsync(ct);
        return entries.Select(AuditEventDto.From).ToList();
    }

    public Task<AuditSeal?> GetLastSealAsync(CancellationToken ct) =>
        db.Seals.AsNoTracking().OrderByDescending(s => s.UpToSequence).FirstOrDefaultAsync(ct);

    public IAsyncEnumerable<AuditEntry> StreamChainAsync(long afterSequence, CancellationToken ct) =>
        db.Entries.AsNoTracking()
            .Where(e => e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .AsAsyncEnumerable();
}
