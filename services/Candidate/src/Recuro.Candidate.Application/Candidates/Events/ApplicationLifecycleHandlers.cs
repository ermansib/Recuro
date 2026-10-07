using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Domain.Candidates;

namespace Recuro.Candidate.Application.Candidates.Events;

/// <summary>Retention rules for candidates (FRD §14). Per-tenant values move to the Config service later.</summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>How long an unsuccessful candidate's data is kept when the event doesn't say.</summary>
    public int UnsuccessfulRetentionDays { get; set; } = 365;
}

// Each consumer declares only the fields it reads (tolerant reader); see services/contracts/events.

/// <summary><c>pipeline.application.created.v1</c>.</summary>
public sealed record ApplicationCreatedPayload(string AppId, string CandidateId);

/// <summary><c>pipeline.application.final_rejected.v1</c>.</summary>
public sealed record ApplicationFinalRejectedPayload(string AppId, string CandidateId, DateOnly? RetainUntil);

/// <summary><c>pipeline.stage.changed.v1</c>.</summary>
public sealed record ApplicationStageChangedPayload(string AppId, string CandidateId, string To);

/// <summary>Keeps the candidate's retention status in step with their applications in the Pipeline service.</summary>
public sealed class ApplicationLifecycleHandler(
    ICandidateRepository candidates,
    IUnitOfWork unitOfWork,
    IOptions<RetentionOptions> retention)
    : IIntegrationEventHandler<ApplicationCreatedPayload>,
      IIntegrationEventHandler<ApplicationFinalRejectedPayload>,
      IIntegrationEventHandler<ApplicationStageChangedPayload>
{
    private static readonly HashSet<string> HiredStages = new(StringComparer.Ordinal) { "PreBoarding", "Onboarded", "Confirmed" };

    public Task Handle(IntegrationEvent<ApplicationCreatedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        return UpdateAsync(data.CandidateId, c => c.TrackApplication(data.AppId), ct);
    }

    public Task Handle(IntegrationEvent<ApplicationFinalRejectedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        var retainUntil = data.RetainUntil ?? DefaultRetainUntil(integrationEvent.Metadata.Time);
        return UpdateAsync(data.CandidateId, c => c.CloseApplication(data.AppId, ApplicationOutcome.Unsuccessful, retainUntil), ct);
    }

    public Task Handle(IntegrationEvent<ApplicationStageChangedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        if (data.To == "Withdrawn")
        {
            var retainUntil = DefaultRetainUntil(integrationEvent.Metadata.Time);
            return UpdateAsync(data.CandidateId, c => c.CloseApplication(data.AppId, ApplicationOutcome.Unsuccessful, retainUntil), ct);
        }

        return HiredStages.Contains(data.To)
            ? UpdateAsync(data.CandidateId, c => c.CloseApplication(data.AppId, ApplicationOutcome.Hired, null), ct)
            : Task.CompletedTask;
    }

    private DateOnly DefaultRetainUntil(DateTimeOffset at) =>
        DateOnly.FromDateTime(at.UtcDateTime).AddDays(retention.Value.UnsuccessfulRetentionDays);

    private async Task UpdateAsync(string candidateId, Action<Domain.Candidates.Candidate> change, CancellationToken ct)
    {
        // An id this tenant doesn't know (or not a candidate id at all) has nothing to update.
        if (!Guid.TryParse(candidateId, out var id) || await candidates.GetAsync(id, ct) is not { } candidate)
        {
            return;
        }

        change(candidate);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
