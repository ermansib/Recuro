using Microsoft.EntityFrameworkCore;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Applications;
using Recuro.Offer.Domain.Bgv;
using Recuro.Offer.Domain.Letters;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="OfferDbContext"/> scopes every query.</summary>
internal sealed class OfferRepository(OfferDbContext db) : IOfferRepository
{
    public Task<JobOffer?> GetAsync(Guid id, CancellationToken ct) =>
        db.Offers.FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<JobOffer?> GetByWorkflowAsync(Guid workflowInstanceId, CancellationToken ct) =>
        db.Offers.FirstOrDefaultAsync(o => o.WorkflowInstanceId == workflowInstanceId, ct);

    public async Task<IReadOnlyList<JobOffer>> ListForApplicationAsync(string appId, CancellationToken ct) =>
        await db.Offers.Where(o => o.AppId == appId).OrderByDescending(o => o.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<JobOffer>> ListAsync(OfferState? state, int limit, CancellationToken ct)
    {
        var query = db.Offers.AsNoTracking();
        if (state is { } s)
        {
            query = query.Where(o => o.State == s);
        }

        return await query.OrderByDescending(o => o.CreatedAt).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<JobOffer>> ListLifecycleDueAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        await db.Offers
            .Where(o => o.State == OfferState.Sent && ((o.NextChaseAt != null && o.NextChaseAt <= now) || (o.ExpiresAt != null && o.ExpiresAt <= now)))
            .OrderBy(o => o.NextChaseAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<int> CountAsync(IReadOnlyCollection<OfferState> states, CancellationToken ct)
    {
        var list = states.ToList();
        return await db.Offers.CountAsync(o => list.Contains(o.State), ct);
    }

    public async Task<IReadOnlyList<JobOffer>> ListForCandidateAsync(string candidateId, CancellationToken ct) =>
        await db.Offers.Where(o => o.CandidateId == candidateId).ToListAsync(ct);

    public void Add(JobOffer offer) => db.Offers.Add(offer);
}

internal sealed class ApplicationTrackRepository(OfferDbContext db) : IApplicationTrackRepository
{
    public async Task<ApplicationTrack?> GetAsync(string appId, CancellationToken ct) =>
        db.ApplicationTracks.Local.FirstOrDefault(t => t.AppId == appId)
        ?? await db.ApplicationTracks.FirstOrDefaultAsync(t => t.AppId == appId, ct);

    public void Add(ApplicationTrack track) => db.ApplicationTracks.Add(track);
}

internal sealed class BgvTrackRepository(OfferDbContext db) : IBgvTrackRepository
{
    public async Task<BgvTrack?> GetAsync(string appId, CancellationToken ct) =>
        db.BgvTracks.Local.FirstOrDefault(t => t.AppId == appId)
        ?? await db.BgvTracks.FirstOrDefaultAsync(t => t.AppId == appId, ct);

    public void Add(BgvTrack track) => db.BgvTracks.Add(track);
}

internal sealed class OfferDocumentRepository(OfferDbContext db) : IOfferDocumentRepository
{
    public async Task<OfferDocument?> GetLatestAsync(Guid offerId, OfferDocumentKind kind, CancellationToken ct) =>
        db.Documents.Local.Where(d => d.OfferId == offerId && d.Kind == kind).MaxBy(d => d.LetterVersion)
        ?? await db.Documents.Where(d => d.OfferId == offerId && d.Kind == kind).OrderByDescending(d => d.LetterVersion).FirstOrDefaultAsync(ct);

    public void Add(OfferDocument document) => db.Documents.Add(document);
}
