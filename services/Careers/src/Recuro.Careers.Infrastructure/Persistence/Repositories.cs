using Microsoft.EntityFrameworkCore;
using Recuro.Careers.Application.Abstractions;
using Recuro.Careers.Domain.Applications;
using Recuro.Careers.Domain.Postings;
using Recuro.Careers.Domain.Requisitions;

namespace Recuro.Careers.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="CareersDbContext"/> scopes every query.</summary>
internal sealed class PostingRepository(CareersDbContext db) : IPostingRepository
{
    public Task<JobPosting?> GetByReqIdAsync(string reqId, CancellationToken ct) =>
        db.Postings.FirstOrDefaultAsync(p => p.ReqId == reqId, ct);

    public Task<JobPosting?> GetByPostingIdAsync(string postingId, CancellationToken ct) =>
        db.Postings.AsNoTracking().FirstOrDefaultAsync(p => p.PostingId == postingId, ct);

    public async Task<IReadOnlyList<JobPosting>> ListAsync(CancellationToken ct) =>
        await db.Postings.AsNoTracking().AsSplitQuery().OrderByDescending(p => p.UpdatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<JobPosting>> SearchVisibleAsync(JobSearch search, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = db.Postings.AsNoTracking()
            .Where(p => p.Status == PostingStatus.Published && p.VisibleFrom != null && p.VisibleFrom <= now);

        if (search.Query is { } text)
        {
            var pattern = $"%{Escape(text)}%";
            query = query.Where(p => EF.Functions.ILike(p.Title, pattern)
                || EF.Functions.ILike(p.Qualification, pattern)
                || EF.Functions.ILike(p.Location, pattern));
        }

        if (search.Location is { } location)
        {
            query = query.Where(p => EF.Functions.ILike(p.LocationFilter, Escape(location)));
        }

        if (search.Industry is { } industry)
        {
            query = query.Where(p => p.Industry != null && EF.Functions.ILike(p.Industry, Escape(industry)));
        }

        if (search.After is { } after)
        {
            // Translated to SQL (posting_id > @after), compared in the database's collation like the ORDER BY.
#pragma warning disable CA1309
            query = query.Where(p => p.VisibleFrom < after.VisibleFrom
                || (p.VisibleFrom == after.VisibleFrom && string.Compare(p.PostingId, after.PostingId) > 0));
#pragma warning restore CA1309
        }

        return await query
            .OrderByDescending(p => p.VisibleFrom)
            .ThenBy(p => p.PostingId)
            .Take(search.Limit)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    public void Add(JobPosting posting) => db.Postings.Add(posting);

    /// <summary>LIKE wildcards in user input match literally.</summary>
    private static string Escape(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}

internal sealed class SourcingGateRepository(CareersDbContext db) : ISourcingGateRepository
{
    public Task<SourcingGate?> GetAsync(string reqId, CancellationToken ct) =>
        db.SourcingGates.FirstOrDefaultAsync(g => g.ReqId == reqId, ct);

    public void Add(SourcingGate gate) => db.SourcingGates.Add(gate);
}

internal sealed class PublicApplicationRepository(CareersDbContext db) : IPublicApplicationRepository
{
    public Task<PublicApplication?> GetByAppIdAsync(string appId, CancellationToken ct) =>
        db.Applications.FirstOrDefaultAsync(a => a.AppId == appId, ct);

    public Task<PublicApplication?> FindByClientKeyAsync(string clientKey, CancellationToken ct) =>
        db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.ClientKey == clientKey, ct);

    public Task<PublicApplication?> GetByFinalRejectedEventAsync(Guid eventId, CancellationToken ct) =>
        db.Applications.FirstOrDefaultAsync(a => a.FinalRejectedEventId == eventId, ct);

    public void Add(PublicApplication application) => db.Applications.Add(application);
}
