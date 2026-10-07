using Microsoft.EntityFrameworkCore;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Domain.Candidates;
using CandidateEntity = Recuro.Candidate.Domain.Candidates.Candidate;

namespace Recuro.Candidate.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="CandidateDbContext"/> scopes every query.</summary>
internal sealed class CandidateRepository(CandidateDbContext db) : ICandidateRepository
{
    public Task<CandidateEntity?> GetAsync(Guid id, CancellationToken ct) =>
        db.Candidates.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<CandidateEntity>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.Candidates.AsNoTracking().AsSplitQuery().Where(c => ids.Contains(c.Id)).ToListAsync(ct);

    public Task<CandidateEntity?> FindByFingerprintAsync(ContactFingerprints fingerprints, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        return db.Candidates.AsNoTracking()
            .Where(c => c.PurgedAt == null)
            .Where(c => c.EmailFingerprint == fingerprints.Email || (fingerprints.Phone != null && c.PhoneFingerprint == fingerprints.Phone))
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<CandidateEntity>> ListDueForPurgeAsync(DateOnly today, int limit, CancellationToken ct) =>
        await db.Candidates
            .Where(c => c.PurgedAt == null && !c.LegalHold && c.RetentionStatus == RetentionStatus.Unsuccessful && c.RetainUntil < today)
            .OrderBy(c => c.RetainUntil)
            .Take(limit)
            .ToListAsync(ct);

    public void Add(CandidateEntity candidate) => db.Candidates.Add(candidate);
}
