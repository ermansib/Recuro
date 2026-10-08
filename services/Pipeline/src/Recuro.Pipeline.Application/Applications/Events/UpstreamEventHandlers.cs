using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Pipeline.Application.Abstractions;
using Recuro.Pipeline.Domain.Applications;
using Recuro.Pipeline.Domain.Requisitions;

namespace Recuro.Pipeline.Application.Applications.Events;

// Each consumer declares only the fields it reads (tolerant reader), per the event catalog (RCU-BKD-001 §5).

/// <summary><c>recruitment.sourcing.unlocked.v1</c>.</summary>
public sealed record SourcingUnlockedPayload(string ReqId, DateOnly? TargetClosure);

/// <summary><c>recruitment.mrf.cancelled.v1</c>.</summary>
public sealed record RequisitionCancelledPayload(string ReqId, string? Reason);

/// <summary>Any event about one application: <c>interview.selection.ratified</c>, <c>bgv.cleared</c>, <c>bgv.adverse.flagged</c>, <c>offer.accepted</c>.</summary>
public sealed record ApplicationRefPayload(string AppId);

/// <summary><c>offer.accepted</c>: the application plus the agreed joining date, when the Offer service sends one.</summary>
public sealed record OfferAcceptedPayload(string AppId, DateOnly? JoiningDate);

/// <summary>Keeps the local sourcing gate in step with the Requisition service (RCU-PPL-001).</summary>
public sealed class RequisitionEventsHandler(
    ISourcingGateRepository gates,
    IApplicationRepository applications,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : IIntegrationEventHandler<SourcingUnlockedPayload>, IIntegrationEventHandler<RequisitionCancelledPayload>
{
    public async Task Handle(IntegrationEvent<SourcingUnlockedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        var gate = await gates.GetAsync(data.ReqId, ct);
        if (gate is null)
        {
            gates.Add(SourcingGate.Opened(data.ReqId, data.TargetClosure, clock.GetUtcNow()));
        }
        else
        {
            gate.Open(data.TargetClosure, clock.GetUtcNow());
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>A cancelled requisition closes sourcing and withdraws every application still in progress.</summary>
    public async Task Handle(IntegrationEvent<RequisitionCancelledPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        var now = clock.GetUtcNow();
        var gate = await gates.GetAsync(data.ReqId, ct);
        if (gate is null)
        {
            gates.Add(SourcingGate.Cancelled(data.ReqId, now));
        }
        else
        {
            gate.Cancel(now);
        }

        var actor = Actors.FromEvent(integrationEvent.Metadata);
        foreach (var application in await applications.ListOpenByRequisitionAsync(data.ReqId, ct))
        {
            // Onboarded candidates cannot be withdrawn; the state machine refuses and they stay as they are.
            application.MoveTo(ApplicationStage.Withdrawn, actor, now);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>Moves applications forward when the owning service reports progress (event map in architecture.md).</summary>
public sealed class ProgressEventsHandler(IApplicationRepository applications, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task AdvanceAsync(
        IntegrationEvent<ApplicationRefPayload> integrationEvent,
        ApplicationStage expectedFrom,
        ApplicationStage to,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var application = await applications.GetAsync(integrationEvent.Data.AppId, ct);
        if (application is null)
        {
            return;
        }

        // Only from the expected stage: a late or redelivered event leaves a moved card alone.
        application.Advance(expectedFrom, to, Actors.FromEvent(integrationEvent.Metadata), clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary><c>interview.selection.ratified</c>: Interview → Selection.</summary>
public sealed class SelectionRatifiedHandler(ProgressEventsHandler progress) : IIntegrationEventHandler<ApplicationRefPayload>
{
    public Task Handle(IntegrationEvent<ApplicationRefPayload> integrationEvent, CancellationToken ct) =>
        progress.AdvanceAsync(integrationEvent, ApplicationStage.Interview, ApplicationStage.Selection, ct);
}

/// <summary><c>bgv.cleared</c>: BGV → Offer (the release gate is open).</summary>
public sealed class BgvClearedHandler(ProgressEventsHandler progress) : IIntegrationEventHandler<ApplicationRefPayload>
{
    public Task Handle(IntegrationEvent<ApplicationRefPayload> integrationEvent, CancellationToken ct) =>
        progress.AdvanceAsync(integrationEvent, ApplicationStage.BGV, ApplicationStage.Offer, ct);
}

/// <summary><c>bgv.adverse.flagged</c>: BGV → Hold while the adverse-BGV saga runs (RCU-BKD-001 §6.4).</summary>
public sealed class BgvAdverseFlaggedHandler(ProgressEventsHandler progress) : IIntegrationEventHandler<ApplicationRefPayload>
{
    public Task Handle(IntegrationEvent<ApplicationRefPayload> integrationEvent, CancellationToken ct) =>
        progress.AdvanceAsync(integrationEvent, ApplicationStage.BGV, ApplicationStage.Hold, ct);
}

/// <summary><c>offer.accepted</c>: Offer → PreBoarding, keeping the joining date for the dashboard.</summary>
public sealed class OfferAcceptedHandler(IApplicationRepository applications, IUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<OfferAcceptedPayload>
{
    public async Task Handle(IntegrationEvent<OfferAcceptedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var application = await applications.GetAsync(integrationEvent.Data.AppId, ct);
        if (application is null)
        {
            return;
        }

        application.Advance(ApplicationStage.Offer, ApplicationStage.PreBoarding, Actors.FromEvent(integrationEvent.Metadata), clock.GetUtcNow());
        if (integrationEvent.Data.JoiningDate is { } joiningDate)
        {
            application.RecordExpectedJoining(joiningDate);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
