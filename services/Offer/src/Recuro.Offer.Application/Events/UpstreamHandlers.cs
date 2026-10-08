using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Applications;
using Recuro.Offer.Domain.Bgv;

namespace Recuro.Offer.Application.Events;

/// <summary>The fields of <c>pipeline.stage.changed.v1</c> this service reads (tolerant reader).</summary>
public sealed record StageChangedPayload(string AppId, string ReqId, string CandidateId, string To, DateTimeOffset At);

/// <summary>Keeps <see cref="ApplicationTrack"/> in step with Pipeline, so drafting an offer needs no call to it.</summary>
public sealed class PipelineStageChangedHandler(IApplicationTrackRepository tracks, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<StageChangedPayload>
{
    public async Task Handle(IntegrationEvent<StageChangedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        var track = await tracks.GetAsync(data.AppId, ct);
        if (track is null)
        {
            tracks.Add(ApplicationTrack.Start(data.AppId, data.ReqId, data.CandidateId, data.To, data.At));
        }
        else if (!track.Move(data.To, data.At))
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>
/// The fields of the <c>bgv.*</c> events this service reads (tolerant reader): <c>appId</c> on all of them,
/// <c>checkType</c> on <c>bgv.adverse.flagged</c>, <c>outcome</c> on <c>bgv.resolved</c>.
/// </summary>
public sealed record BgvEventPayload(string AppId, string? CheckType, string? Outcome);

/// <summary>
/// RCU-OFR-007: the release gate, kept from Bgv's events. <c>bgv.case.initiated</c> opens it as in
/// progress, <c>bgv.cleared</c> clears it, <c>bgv.adverse.flagged</c> holds it, and <c>bgv.resolved</c>
/// lifts the hold (an adverse resolution keeps it) until Bgv reports the case cleared.
/// </summary>
public sealed class BgvGateHandler(IBgvTrackRepository tracks, IOfferRepository offers, IUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<BgvEventPayload>
{
    public async Task Handle(IntegrationEvent<BgvEventPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        var at = integrationEvent.Metadata.Time;
        var track = await tracks.GetAsync(data.AppId, ct);
        if (track is null)
        {
            track = BgvTrack.Start(data.AppId, DateTimeOffset.MinValue);
            tracks.Add(track);
        }

        switch (integrationEvent.Metadata.Type)
        {
            case EventTypes.Bgv.Cleared:
                track.Apply(BgvGate.Cleared, [], at);
                break;
            case EventTypes.Bgv.AdverseFlagged:
                var check = string.IsNullOrWhiteSpace(data.CheckType) ? "BGV check" : data.CheckType.Trim();
                track.Apply(BgvGate.Adverse, track.Gate == BgvGate.Adverse ? [.. track.Blockers, check] : [check], at);
                foreach (var offer in (await offers.ListForApplicationAsync(data.AppId, ct)).Where(o => o.IsOpen))
                {
                    offer.NoteBgv($"Adverse BGV finding on {check} — release on hold", clock.GetUtcNow());
                }

                break;
            case EventTypes.Bgv.Resolved:
                var adverse = data.Outcome?.Contains("Adverse", StringComparison.OrdinalIgnoreCase) == true;
                track.Apply(adverse ? BgvGate.Adverse : BgvGate.InProgress, adverse ? track.Blockers : [], at);
                break;
            default:
                // bgv.case.initiated: in progress unless something newer is already known.
                track.Apply(track.Gate, track.Blockers, at);
                break;
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>The fields of <c>candidate.purged.v1</c> this service reads.</summary>
public sealed record CandidatePurgedPayload(string CandidateId);

/// <summary>Retention hygiene: offers keep ids and amounts for audit, but drop the stored name.</summary>
public sealed class CandidatePurgedHandler(IOfferRepository offers, IUnitOfWork unitOfWork) : IIntegrationEventHandler<CandidatePurgedPayload>
{
    public async Task Handle(IntegrationEvent<CandidatePurgedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        foreach (var offer in await offers.ListForCandidateAsync(integrationEvent.Data.CandidateId, ct))
        {
            offer.ScrubCandidate();
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
