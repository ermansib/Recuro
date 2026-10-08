using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Domain.Applications;
using Recuro.Interview.Domain.Interviews;

namespace Recuro.Interview.Application.Events;

/// <summary>The fields of <c>pipeline.stage.changed.v1</c> this service reads (tolerant reader).</summary>
public sealed record StageChangedPayload(string AppId, string ReqId, string CandidateId, string To, DateTimeOffset At);

/// <summary>
/// Keeps <see cref="ApplicationTrack"/> in step with Pipeline: a move to Interview opens scheduling; a
/// rejection or withdrawal cancels rounds still waiting, so nobody is chased for feedback that no longer matters.
/// </summary>
public sealed class PipelineStageChangedHandler(
    IApplicationTrackRepository tracks,
    IInterviewRepository interviews,
    IUnitOfWork unitOfWork) : IIntegrationEventHandler<StageChangedPayload>
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

        if (ApplicationTrack.ClosedStages.Contains(data.To))
        {
            foreach (var round in await interviews.ListForApplicationAsync(data.AppId, ct))
            {
                round.Cancel($"Application {data.To.ToLowerInvariant()}");
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
