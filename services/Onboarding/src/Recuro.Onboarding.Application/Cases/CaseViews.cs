using Recuro.Onboarding.Application.Abstractions;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Application.Cases;

/// <summary>Builds case DTOs with the BGV state Onboarding keeps from <c>bgv.*</c> events.</summary>
internal sealed class CaseViews(IBgvTrackRepository bgv, TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<BgvStatus> BgvStatusAsync(string appId, CancellationToken ct) =>
        (await bgv.GetAsync(appId, ct))?.Status ?? BgvStatus.NotStarted;

    public async Task<OnboardingCaseDto> ToDtoAsync(OnboardingCase onboardingCase, CancellationToken ct) =>
        OnboardingCaseDto.From(onboardingCase, await BgvStatusAsync(onboardingCase.AppId, ct), Today);

    public async Task<IReadOnlyList<OnboardingCaseDto>> ToDtosAsync(IReadOnlyList<OnboardingCase> cases, CancellationToken ct)
    {
        var statuses = await bgv.StatusesAsync(cases.Select(c => c.AppId).Distinct(StringComparer.Ordinal).ToList(), ct);
        var today = Today;
        return cases.Select(c => OnboardingCaseDto.From(c, statuses.GetValueOrDefault(c.AppId, BgvStatus.NotStarted), today)).ToList();
    }
}
