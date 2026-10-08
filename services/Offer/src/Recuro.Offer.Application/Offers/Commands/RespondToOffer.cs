using FluentValidation;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Offers.Commands;

/// <summary>Body of <c>POST /api/v1/offers/{id}/outcome</c> (frontend setOfferOutcome).</summary>
public sealed record OfferOutcomeRequest(string? Outcome, string? Reason);

/// <summary>RCU-OFR-006: HR-TA records the candidate's answer. Accepted starts pre-boarding (<c>offer.accepted</c>).</summary>
public sealed record RecordOfferOutcomeCommand(Guid OfferId, OfferOutcomeRequest Request) : ICommand<VersionedOffer>;

internal sealed class RecordOfferOutcomeCommandValidator : AbstractValidator<RecordOfferOutcomeCommand>
{
    public RecordOfferOutcomeCommandValidator()
    {
        RuleFor(c => c.Request).NotNull();
        RuleFor(c => c.Request.Outcome).Must(o => o is "Accepted" or "Declined").WithMessage("outcome is Accepted or Declined.").OverridePropertyName("outcome");
        RuleFor(c => c.Request.Reason).MaximumLength(OfferLimits.LongText).OverridePropertyName("reason");
    }
}

internal sealed class RecordOfferOutcomeCommandHandler(OfferWriter writer) : ICommandHandler<RecordOfferOutcomeCommand, VersionedOffer>
{
    public Task<Result<VersionedOffer>> Handle(RecordOfferOutcomeCommand command, CancellationToken ct) =>
        writer.WriteAsync(
            command.OfferId,
            null,
            offer => command.Request.Outcome == "Accepted"
                ? offer.Accept(writer.Actor, writer.Now)
                : offer.Decline(command.Request.Reason, writer.Actor, writer.Now),
            ct);
}

/// <summary>Body of <c>POST /api/v1/offers/{id}/withdraw</c>.</summary>
public sealed record WithdrawOfferRequest(string? Reason);

/// <summary>RCU-OFR-006: HR Head (or MD/CEO per §16) withdraws an offer with a documented reason.</summary>
public sealed record WithdrawOfferCommand(Guid OfferId, WithdrawOfferRequest Request) : ICommand<VersionedOffer>;

internal sealed class WithdrawOfferCommandValidator : AbstractValidator<WithdrawOfferCommand>
{
    public WithdrawOfferCommandValidator()
    {
        RuleFor(c => c.Request).NotNull();
        RuleFor(c => c.Request.Reason).MaximumLength(OfferLimits.LongText).OverridePropertyName("reason");
    }
}

internal sealed class WithdrawOfferCommandHandler(OfferWriter writer, Abstractions.IWorkflowClient workflows) : ICommandHandler<WithdrawOfferCommand, VersionedOffer>
{
    public async Task<Result<VersionedOffer>> Handle(WithdrawOfferCommand command, CancellationToken ct)
    {
        Guid? open = null;
        var result = await writer.WriteAsync(
            command.OfferId,
            null,
            offer =>
            {
                open = offer.State == OfferState.PendingApproval ? offer.WorkflowInstanceId : null;
                return offer.Withdraw(command.Request.Reason, writer.Actor, writer.Now);
            },
            ct);

        if (result.IsSuccess && open is { } instanceId)
        {
            // Best effort: a late decision on a withdrawn offer is ignored anyway.
            try
            {
                await workflows.CancelAsync(instanceId, "The offer was withdrawn.", ct);
            }
            catch (Abstractions.DependencyUnavailableException)
            {
            }
        }

        return result;
    }
}
