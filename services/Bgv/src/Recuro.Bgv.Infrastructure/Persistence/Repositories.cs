using Microsoft.EntityFrameworkCore;
using Recuro.Bgv.Application.Abstractions;
using Recuro.Bgv.Domain.Cases;
using Recuro.Bgv.Domain.Requests;

namespace Recuro.Bgv.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="BgvDbContext"/> scopes every query.</summary>
internal sealed class BgvCaseRepository(BgvDbContext db) : IBgvCaseRepository
{
    private static readonly CaseStatus[] Running = [CaseStatus.Open, CaseStatus.UnderReview];

    public async Task<BgvCase?> FindAsync(string caseOrAppId, CancellationToken ct)
    {
        var byApp = await db.Cases.FirstOrDefaultAsync(c => c.AppId == caseOrAppId, ct);
        if (byApp is not null || !Guid.TryParse(caseOrAppId, out var id))
        {
            return byApp;
        }

        return await db.Cases.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public Task<bool> ExistsForApplicationAsync(string appId, CancellationToken ct) =>
        db.Cases.AnyAsync(c => c.AppId == appId, ct);

    public async Task<IReadOnlyList<BgvCase>> ListOpenByVendorAsync(string vendorId, CancellationToken ct) =>
        await db.Cases.Where(c => c.VendorId == vendorId && Running.Contains(c.Status)).OrderBy(c => c.InitiatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<BgvCase>> ListNeedingReassignmentAsync(CancellationToken ct) =>
        await db.Cases.AsNoTracking().Where(c => c.NeedsReassignment && Running.Contains(c.Status)).OrderBy(c => c.DueAt).ToListAsync(ct);

    public async Task<(int InProgress, int DueSoon)> CountInProgressAsync(DateTimeOffset dueBefore, CancellationToken ct)
    {
        var counts = await db.Cases.Where(c => Running.Contains(c.Status))
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), DueSoon = g.Count(c => c.DueAt <= dueBefore) })
            .FirstOrDefaultAsync(ct);
        return counts is null ? (0, 0) : (counts.Total, counts.DueSoon);
    }

    public void Add(BgvCase bgvCase) => db.Cases.Add(bgvCase);
}

internal sealed class BgvRequestRepository(BgvDbContext db) : IBgvRequestRepository
{
    public Task<BgvRequest?> GetAsync(string appId, CancellationToken ct) =>
        db.Requests.FirstOrDefaultAsync(r => r.AppId == appId, ct);

    public void Add(BgvRequest request) => db.Requests.Add(request);
}
