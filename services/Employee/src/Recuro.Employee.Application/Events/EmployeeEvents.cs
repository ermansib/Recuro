using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Employee.Application.Abstractions;
using Recuro.Employee.Domain.Ijp;
using Recuro.Employee.Domain.Referrals;
using Recuro.Employee.Domain.Requisitions;

namespace Recuro.Employee.Application.Events;

// Payloads carry ids, never personal data (services/contracts/events/*.schema.json).

/// <summary><c>employee.ijp.applied.v1</c> (RCU-BKD-001 §5).</summary>
public sealed record IjpAppliedPayload(string EmployeeId, string ReqId, string AppId, string CandidateId);

/// <summary><c>employee.referral.submitted.v1</c> (RCU-BKD-001 §5).</summary>
public sealed record ReferralSubmittedPayload(
    Guid ReferralId,
    string ReferrerId,
    string ReqId,
    string AppId,
    string CandidateId,
    string Relationship,
    bool BonusEligible,
    bool PossibleDuplicate);

/// <summary>Turns finished intakes into integration events, in the same transaction.</summary>
internal sealed class PublishEmployeeEvents(IIntegrationEventPublisher publisher)
    : IDomainEventHandler<InternalApplicationCompleted>, IDomainEventHandler<ReferralCompleted>
{
    public Task Handle(InternalApplicationCompleted domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var a = domainEvent.Application;
        publisher.Publish(
            EventTypes.Employee.IjpApplied,
            $"Application/{a.AppId}",
            new IjpAppliedPayload(a.EmployeeId, a.ReqId, a.AppId!, a.CandidateId!));
        return Task.CompletedTask;
    }

    public Task Handle(ReferralCompleted domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var r = domainEvent.Referral;
        publisher.Publish(
            EventTypes.Employee.ReferralSubmitted,
            $"Referral/{r.Id}",
            new ReferralSubmittedPayload(
                r.Id, r.EmployeeId, r.ReqId, r.AppId!, r.CandidateId!, r.Relationship.ToString(), r.BonusEligible, r.DuplicateOf is not null));
        return Task.CompletedTask;
    }
}

// Each consumer declares only the fields it reads (tolerant reader).

/// <summary><c>recruitment.sourcing.unlocked.v1</c>.</summary>
public sealed record SourcingUnlockedPayload(string ReqId);

/// <summary><c>recruitment.mrf.cancelled.v1</c>.</summary>
public sealed record RequisitionCancelledPayload(string ReqId);

/// <summary><c>pipeline.stage.changed.v1</c>.</summary>
public sealed record StageChangedPayload(string AppId, string To);

/// <summary><c>pipeline.application.final_rejected.v1</c>.</summary>
public sealed record FinalRejectedPayload(string AppId);

/// <summary>Keeps the local sourcing gate in step with Requisition and withdraws IJP postings of cancelled requisitions.</summary>
public sealed class RequisitionEventsHandler(
    ISourcingGateRepository gates,
    IIjpPostingRepository postings,
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
        var reqId = integrationEvent.Data.ReqId;
        var gate = await gates.GetAsync(reqId, ct);
        if (gate is null)
        {
            gates.Add(SourcingGate.Cancelled(reqId));
        }
        else
        {
            gate.Cancel();
        }

        (await postings.GetAsync(reqId, ct))?.Withdraw(integrationEvent.Metadata.Time);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>RCU-EMP-005: mirrors the coarse progress of IJP applications and referrals. Other applications are ignored.</summary>
public sealed class IntakeProgressHandler(IIntakeRepository intake, IUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<StageChangedPayload>, IIntegrationEventHandler<FinalRejectedPayload>
{
    public Task Handle(IntegrationEvent<StageChangedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return MoveAsync(integrationEvent.Data.AppId, integrationEvent.Data.To, ct);
    }

    public Task Handle(IntegrationEvent<FinalRejectedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return MoveAsync(integrationEvent.Data.AppId, "Rejected", ct);
    }

    private async Task MoveAsync(string appId, string stage, CancellationToken ct)
    {
        var record = await intake.GetByAppIdAsync(appId, ct);
        if (record is null)
        {
            return;
        }

        record.StageChanged(stage, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
    }
}
