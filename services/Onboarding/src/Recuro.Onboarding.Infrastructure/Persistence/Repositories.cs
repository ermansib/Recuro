using Microsoft.EntityFrameworkCore;
using Recuro.Onboarding.Application.Abstractions;
using Recuro.Onboarding.Domain.Bgv;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="OnboardingDbContext"/> scopes every query.</summary>
internal sealed class OnboardingCaseRepository(OnboardingDbContext db) : IOnboardingCaseRepository
{
    private static readonly CaseStatus[] Running = [CaseStatus.PreBoarding, CaseStatus.Day1Ready];

    public async Task<OnboardingCase?> FindAsync(string caseOrAppId, CancellationToken ct)
    {
        var byApp = await db.Cases.Where(c => c.AppId == caseOrAppId).OrderByDescending(c => c.AcceptedAt).FirstOrDefaultAsync(ct);
        if (byApp is not null || !Guid.TryParse(caseOrAppId, out var id))
        {
            return byApp;
        }

        return await db.Cases.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public Task<OnboardingCase?> FindRunningAsync(string appId, CancellationToken ct) =>
        db.Cases.Where(c => c.AppId == appId && c.Status != CaseStatus.Cancelled).OrderByDescending(c => c.AcceptedAt).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<OnboardingCase>> ListAsync(CaseStatus? status, int limit, CancellationToken ct)
    {
        var query = db.Cases.AsNoTracking().AsSplitQuery();
        query = status is { } s ? query.Where(c => c.Status == s) : query.Where(c => Running.Contains(c.Status));
        return await query.OrderBy(c => c.JoiningDate).ThenBy(c => c.AcceptedAt).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OnboardingCase>> ListWithDueMilestonesAsync(DateOnly today, int limit, CancellationToken ct) =>
        await db.Cases.AsSplitQuery()
            .Where(c => Running.Contains(c.Status) && c.Milestones.Any(m => m.Status == MilestoneStatus.Scheduled && m.DueOn <= today))
            .OrderBy(c => c.JoiningDate)
            .Take(limit)
            .ToListAsync(ct);

    public void Add(OnboardingCase onboardingCase) => db.Cases.Add(onboardingCase);
}

internal sealed class BgvTrackRepository(OnboardingDbContext db) : IBgvTrackRepository
{
    public Task<BgvTrack?> GetAsync(string appId, CancellationToken ct) =>
        db.BgvTracks.FirstOrDefaultAsync(t => t.AppId == appId, ct);

    public async Task<IReadOnlyDictionary<string, BgvStatus>> StatusesAsync(IReadOnlyCollection<string> appIds, CancellationToken ct) =>
        await db.BgvTracks.AsNoTracking().Where(t => appIds.Contains(t.AppId)).ToDictionaryAsync(t => t.AppId, t => t.Status, StringComparer.Ordinal, ct);

    public void Add(BgvTrack track) => db.BgvTracks.Add(track);
}
