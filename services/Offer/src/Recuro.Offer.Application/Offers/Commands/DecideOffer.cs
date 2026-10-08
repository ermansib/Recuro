using FluentValidation;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Offers.Commands;

/// <summary>Body of <c>POST /api/v1/offers/{id}/approve</c>. Without a body it approves.</summary>
public sealed record DecideOfferRequest(string? ActionId, string? Reason);

/// <summary>
/// RCU-OFR-003 (frontend approveOffer): the approver decides from the offer screen. It decides the same
/// workflow task the inbox shows, so one decision counts once whichever screen it came from.
/// </summary>
public sealed record DecideOfferCommand(Guid OfferId, DecideOfferRequest Request) : ICommand<VersionedOffer>;

internal sealed class DecideOfferCommandValidator : AbstractValidator<DecideOfferCommand>
{
    public DecideOfferCommandValidator()
    {
        RuleFor(c => c.Request).NotNull();
        RuleFor(c => c.Request.ActionId).Must(a => a is null or "approve" or "decline").WithMessage("actionId is approve or decline.").OverridePropertyName("actionId");
        RuleFor(c => c.Request.Reason).MaximumLength(OfferLimits.LongText).OverridePropertyName("reason");
    }
}

internal sealed class DecideOfferCommandHandler(OfferWriter writer, IWorkflowClient workflows, IUnitOfWork unitOfWork)
    : ICommandHandler<DecideOfferCommand, VersionedOffer>
{
    public async Task<Result<VersionedOffer>> Handle(DecideOfferCommand command, CancellationToken ct)
    {
        var loaded = await writer.LoadAsync(command.OfferId, null, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var offer = loaded.Value;
        var approve = command.Request.ActionId is null or "approve";
        if (offer.State != OfferState.PendingApproval || offer.WorkflowInstanceId is not { } instanceId)
        {
            // Already decided the same way (from the inbox): nothing to do.
            return (approve && offer.State == OfferState.Approved) ? await writer.ViewAsync(offer, ct) : OfferErrors.NotPendingApproval(offer.Id, offer.State);
        }

        if (!approve && (command.Request.Reason?.Trim().Length ?? 0) < OfferLimits.MinReasonLength)
        {
            return OfferErrors.ReasonRequired();
        }

        var instance = await workflows.GetAsync(instanceId, ct);
        var task = instance?.Tasks.FirstOrDefault(t => t.Status == "Open");
        if (task is null)
        {
            return OfferErrors.NoOpenApproval(offer.Id);
        }

        var decided = await workflows.DecideAsync(task.Id, approve ? "approve" : "decline", command.Request.Reason, ct);
        if (!decided.Accepted)
        {
            return decided.StatusCode switch
            {
                403 => Error.Forbidden("not_the_approver", $"This offer is waiting for {offer.Route?.Label ?? "the resolved approver"}."),
                409 => Error.Conflict("already_decided", "This approval was already decided."),
                400 => Error.Validation("decision_invalid", decided.Detail ?? "Workflow rejected the decision."),
                _ => throw new DependencyUnavailableException($"Workflow answered {decided.StatusCode} to a decision."),
            };
        }

        // Single-leg route: the decision finishes the instance. Apply it now; the completion event is then a no-op.
        var applied = offer.ApplyDecision(approve, writer.Actor, command.Request.Reason, writer.Now);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await writer.ViewAsync(offer, ct);
    }
}

/// <summary>The fields of <c>workflow.task.completed.v1</c> this service reads (tolerant reader).</summary>
public sealed record WorkflowTaskCompletedPayload(Guid? InstanceId, string? Type, string? SubjectType, string? SubjectId, string? Reason, string? InstanceStatus);

/// <summary>RCU-OFR-003: an offer approval finished in the inbox. Approved → <c>offer.approved</c>; declined → back to HR-TA.</summary>
public sealed partial class OfferApprovalCompletedHandler(
    IOfferRepository offers,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<OfferApprovalCompletedHandler> logger) : IIntegrationEventHandler<WorkflowTaskCompletedPayload>
{
    public async Task Handle(IntegrationEvent<WorkflowTaskCompletedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        if (data.SubjectType != SubmitOfferCommandHandler.SubjectType || data.InstanceId is null || data.InstanceStatus is not ("Approved" or "Rejected"))
        {
            return;
        }

        var offer = await offers.GetByWorkflowAsync(data.InstanceId.Value, ct);
        if (offer is null)
        {
            // Superseded by a revision or resubmission, or another tenant's: nothing to do.
            Unknown(logger, data.InstanceId.Value, integrationEvent.Metadata.Id);
            return;
        }

        var by = integrationEvent.Metadata.ActorName ?? "approver";
        var applied = offer.ApplyDecision(data.InstanceStatus == "Approved", by, data.Reason, clock.GetUtcNow());
        if (applied.IsFailure)
        {
            Ignored(logger, offer.Id, offer.State, applied.Error!.Message);
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Offer approval completed for unknown workflow {InstanceId} (event {EventId})")]
    private static partial void Unknown(ILogger logger, Guid instanceId, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Approval outcome for offer {OfferId} ignored in state {State}: {Reason}")]
    private static partial void Ignored(ILogger logger, Guid offerId, OfferState state, string reason);
}
