using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Careers.Application.Abstractions;
using Recuro.Careers.Domain.Applications;
using Recuro.Careers.Domain.Postings;
using Recuro.Careers.Domain.Requisitions;

namespace Recuro.Careers.Application.Events;

// Payloads carry ids, never personal data (services/contracts/events/*.schema.json).

/// <summary>Consent wording versions given on the form.</summary>
public sealed record AppliedConsentsPayload(string DataPrivacy, string ConflictOfInterest);

/// <summary><c>career.job.applied.v1</c> (RCU-BKD-001 §5): appId, jobId and consents, plus the ids consumers need.</summary>
public sealed record JobAppliedPayload(string AppId, string JobId, string ReqId, string CandidateId, bool PossibleDuplicate, AppliedConsentsPayload Consents);

/// <summary>Turns the finished intake into <c>career.job.applied.v1</c>, in the same transaction.</summary>
internal sealed class PublishCareersEvents(IIntegrationEventPublisher publisher) : IDomainEventHandler<PublicApplicationCompleted>
{
    public Task Handle(PublicApplicationCompleted domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var a = domainEvent.Application;
        string Version(string type) => a.Consents.FirstOrDefault(c => c.Type == type)?.TextVersion ?? string.Empty;
        publisher.Publish(
            EventTypes.Career.JobApplied,
            $"Application/{a.AppId}",
            new JobAppliedPayload(
                a.AppId!,
                a.PostingId,
                a.ReqId,
                a.CandidateId!,
                !a.CandidateCreatedHere,
                new AppliedConsentsPayload(Version(Applications.ConsentTypes.DataPrivacy), Version(Applications.ConsentTypes.ConflictOfInterest))));
        return Task.CompletedTask;
    }
}

// Each consumer declares only the fields it reads (tolerant reader).

/// <summary><c>recruitment.sourcing.unlocked.v1</c>.</summary>
public sealed record SourcingUnlockedPayload(string ReqId);

/// <summary><c>recruitment.mrf.cancelled.v1</c>.</summary>
public sealed record RequisitionCancelledPayload(string ReqId, string? Reason);

/// <summary><c>pipeline.stage.changed.v1</c>.</summary>
public sealed record StageChangedPayload(string AppId, string To);

/// <summary><c>pipeline.application.final_rejected.v1</c>.</summary>
public sealed record FinalRejectedPayload(string AppId, DateOnly RegretSendAt);

/// <summary><c>notification.email.dispatched.v1</c>.</summary>
public sealed record EmailDispatchedPayload(string TemplateKey, Guid SourceEventId);

/// <summary>Keeps the local sourcing gate in step with Requisition, and closes postings of cancelled requisitions (RCU-CAR-007).</summary>
public sealed class RequisitionEventsHandler(
    ISourcingGateRepository gates,
    IPostingRepository postings,
    IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<SourcingUnlockedPayload>, IIntegrationEventHandler<RequisitionCancelledPayload>
{
    public async Task Handle(IntegrationEvent<SourcingUnlockedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var reqId = integrationEvent.Data.ReqId;
        var gate = await gates.GetAsync(reqId, ct);
        if (gate is null)
        {
            gates.Add(SourcingGate.Opened(reqId, integrationEvent.Metadata.Time));
        }
        else
        {
            gate.Open(integrationEvent.Metadata.Time);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task Handle(IntegrationEvent<RequisitionCancelledPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        var gate = await gates.GetAsync(data.ReqId, ct);
        if (gate is null)
        {
            gates.Add(SourcingGate.Cancelled(data.ReqId));
        }
        else
        {
            gate.Cancel();
        }

        var posting = await postings.GetByReqIdAsync(data.ReqId, ct);
        var metadata = integrationEvent.Metadata;
        posting?.Close(
            string.IsNullOrWhiteSpace(data.Reason) ? "Requisition cancelled" : $"Requisition cancelled: {data.Reason}",
            new PostingActor(metadata.ActorId, metadata.ActorName ?? metadata.Source),
            metadata.Time);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>
/// RCU-CAR-005/006: keeps the coarse status of portal applications current and logs the regret email's
/// delivery. Events about applications that did not come through the careers site are ignored.
/// </summary>
public sealed class ApplicationProgressHandler(IPublicApplicationRepository applications, IUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<StageChangedPayload>,
      IIntegrationEventHandler<FinalRejectedPayload>,
      IIntegrationEventHandler<EmailDispatchedPayload>
{
    /// <summary>Notification's template key for the regret email.</summary>
    public const string RegretTemplate = "candidate.regret";

    public async Task Handle(IntegrationEvent<StageChangedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var application = await applications.GetByAppIdAsync(integrationEvent.Data.AppId, ct);
        if (application is null)
        {
            return;
        }

        application.StageChanged(integrationEvent.Data.To, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task Handle(IntegrationEvent<FinalRejectedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var application = await applications.GetByAppIdAsync(integrationEvent.Data.AppId, ct);
        if (application is null)
        {
            return;
        }

        application.FinallyRejected(integrationEvent.Metadata.Id, integrationEvent.Data.RegretSendAt, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task Handle(IntegrationEvent<EmailDispatchedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!string.Equals(integrationEvent.Data.TemplateKey, RegretTemplate, StringComparison.Ordinal))
        {
            return;
        }

        var application = await applications.GetByFinalRejectedEventAsync(integrationEvent.Data.SourceEventId, ct);
        if (application is null)
        {
            return;
        }

        application.RegretDelivered(integrationEvent.Metadata.Time);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
