using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Onboarding.Application.Abstractions;
using Recuro.Onboarding.Domain.Bgv;
using Recuro.Onboarding.Domain.Cases;
using Recuro.Onboarding.Domain.Rules;

namespace Recuro.Onboarding.Application.Events;

// Each consumer declares only the fields it reads (tolerant reader), per the event catalog (RCU-BKD-001 §5).
// The inbox processor commits each handler's changes with its inbox row; a handler throws to retry.

/// <summary>
/// The fields of <c>offer.accepted.v1</c> this service reads. <c>probationMonths</c> is optional
/// (requested from Offer); without it the onboarding rules' probation length applies.
/// </summary>
public sealed record OfferAcceptedPayload(string? OfferId, string AppId, string? ReqId, string? CandidateId, DateOnly? JoiningDate, int? ProbationMonths);

/// <summary>The fields of <c>offer.withdrawn.v1</c> this service reads.</summary>
public sealed record OfferWithdrawnPayload(string? OfferId, string AppId, string? From, string? Reason);

/// <summary><c>bgv.case.initiated.v1</c>.</summary>
public sealed record BgvCaseInitiatedPayload(string? CaseId, string AppId);

/// <summary><c>bgv.adverse.flagged.v1</c>.</summary>
public sealed record BgvAdverseFlaggedPayload(string? CaseId, string AppId);

/// <summary><c>bgv.cleared.v1</c>.</summary>
public sealed record BgvClearedPayload(string? CaseId, string AppId, DateTimeOffset? ClearedAt);

/// <summary>
/// <c>bgv.resolved.v1</c>: <c>ResolvedAdverse</c> is final; <c>ResolvedCleared</c> lifts the hold and the
/// case waits for <c>bgv.cleared</c> like any other.
/// </summary>
public sealed record BgvResolvedPayload(string? CaseId, string AppId, string? Status);

/// <summary>
/// RCU-ONB-001, the Offer → Joining saga's hand-off (§6.3): an accepted offer opens the onboarding
/// case. The joining-instructions email goes out now; the T-21 and T-7 touchpoints land on the latest
/// working day on or before their date, and the IT/Admin ticket 5 working days before joining, all on
/// the tenant's business calendar.
/// </summary>
public sealed partial class OfferAcceptedHandler(
    IOnboardingCaseRepository cases,
    IOnboardingRules rules,
    IWorkingDays workingDays,
    TimeProvider clock,
    ILogger<OfferAcceptedHandler> logger) : IIntegrationEventHandler<OfferAcceptedPayload>
{
    public async Task Handle(IntegrationEvent<OfferAcceptedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        if (data.JoiningDate is not { } joining)
        {
            // Offer always sends it; without it there is nothing to schedule against.
            NoJoiningDate(logger, data.AppId, integrationEvent.Metadata.Id);
            return;
        }

        if (await cases.FindRunningAsync(data.AppId, ct) is not null)
        {
            return;
        }

        var template = await rules.GetTemplateAsync(ct);
        var preBoarding = new List<PlannedMilestone>();
        foreach (var daysBefore in template.EngagementDaysBefore.Order().Reverse())
        {
            var dueOn = await workingDays.OnOrBeforeAsync(joining.AddDays(-daysBefore), ct);
            preBoarding.Add(new PlannedMilestone(MilestoneKinds.Engagement(daysBefore), $"Engagement touchpoint (T-{daysBefore}d)", MilestonePhase.PreBoarding, dueOn));
        }

        var provisionOn = await workingDays.SubtractAsync(joining, template.ProvisioningWorkingDaysBefore, ct);
        preBoarding.Add(new PlannedMilestone(MilestoneKinds.ItProvisioning, $"IT / Admin Day-1 setup (T-{template.ProvisioningWorkingDaysBefore} working days)", MilestonePhase.PreBoarding, provisionOn));

        var offer = new AcceptedOffer(data.AppId, data.OfferId, data.ReqId ?? string.Empty, data.CandidateId ?? string.Empty, joining, data.ProbationMonths);
        cases.Add(OnboardingCase.Start(offer, template, preBoarding, clock.GetUtcNow()));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "offer.accepted for {AppId} has no joining date; no onboarding case opened (event {EventId})")]
    private static partial void NoJoiningDate(ILogger logger, string appId, Guid eventId);
}

/// <summary>
/// Saga compensation (§6.3): an offer withdrawn after acceptance cancels the case, stops every reminder
/// and cancels the IT/Admin ticket.
/// </summary>
public sealed partial class OfferWithdrawnHandler(
    IOnboardingCaseRepository cases,
    IProvisioningAdapter provisioning,
    TimeProvider clock,
    ILogger<OfferWithdrawnHandler> logger) : IIntegrationEventHandler<OfferWithdrawnPayload>
{
    public async Task Handle(IntegrationEvent<OfferWithdrawnPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        var onboardingCase = await cases.FindRunningAsync(data.AppId, ct);
        if (onboardingCase is null || (data.OfferId is not null && onboardingCase.OfferId is not null && data.OfferId != onboardingCase.OfferId))
        {
            return;
        }

        if (onboardingCase.Status == CaseStatus.Confirmed)
        {
            ConfirmedNotCancelled(logger, onboardingCase.Id, integrationEvent.Metadata.Id);
            return;
        }

        var reason = string.IsNullOrWhiteSpace(data.Reason) ? "Offer withdrawn" : $"Offer withdrawn — {data.Reason.Trim()}";
        var tickets = onboardingCase.Cancel(reason[..Math.Min(reason.Length, 2000)], clock.GetUtcNow());
        foreach (var ticket in tickets)
        {
            // Idempotent at the adapter; a failure throws and the whole event is retried.
            await provisioning.CancelTicketAsync(ticket, ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Offer withdrawn for confirmed onboarding case {CaseId}; left as is (event {EventId})")]
    private static partial void ConfirmedNotCancelled(ILogger logger, Guid caseId, Guid eventId);
}

/// <summary>
/// Keeps the BGV state of each application (RCU-ONB-003, BGV-009): pending or under review blocks
/// confirmation but not joining; only <c>bgv.resolved</c> clears an adverse hold.
/// </summary>
public sealed class BgvStatusHandler(IBgvTrackRepository tracks)
    : IIntegrationEventHandler<BgvCaseInitiatedPayload>,
      IIntegrationEventHandler<BgvAdverseFlaggedPayload>,
      IIntegrationEventHandler<BgvClearedPayload>,
      IIntegrationEventHandler<BgvResolvedPayload>
{
    public const string ResolvedCleared = "ResolvedCleared";

    public Task Handle(IntegrationEvent<BgvCaseInitiatedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return ApplyAsync(integrationEvent.Data.AppId, integrationEvent.Data.CaseId, BgvStatus.Pending, integrationEvent.Metadata.Time, ct);
    }

    public Task Handle(IntegrationEvent<BgvAdverseFlaggedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return ApplyAsync(integrationEvent.Data.AppId, integrationEvent.Data.CaseId, BgvStatus.UnderReview, integrationEvent.Metadata.Time, ct);
    }

    public Task Handle(IntegrationEvent<BgvClearedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        return ApplyAsync(data.AppId, data.CaseId, BgvStatus.Cleared, data.ClearedAt ?? integrationEvent.Metadata.Time, ct);
    }

    public Task Handle(IntegrationEvent<BgvResolvedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        var status = data.Status == ResolvedCleared ? BgvStatus.Pending : BgvStatus.Adverse;
        return ApplyAsync(data.AppId, data.CaseId, status, integrationEvent.Metadata.Time, ct);
    }

    private async Task ApplyAsync(string appId, string? caseId, BgvStatus status, DateTimeOffset at, CancellationToken ct)
    {
        var track = await tracks.GetAsync(appId, ct);
        if (track is null)
        {
            tracks.Add(BgvTrack.Start(appId, caseId, status, at));
        }
        else
        {
            track.Apply(status, caseId, at);
        }
    }
}
