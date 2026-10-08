using FluentValidation;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Offers.Commands;

/// <summary>Body of <c>PUT /api/v1/offers/{id}/components</c> (frontend updateOfferComponents).</summary>
public sealed record ReviseComponentsRequest(decimal? Fixed, decimal? Variable, decimal? Benefits, string? Reason);

/// <summary>
/// RCU-OFR-001/005: revise the CTC. The rule-set runs again and the trail records the change. A revision
/// after submission voids the open approval (the workflow is cancelled) and returns the offer to Draft.
/// </summary>
public sealed record ReviseComponentsCommand(Guid OfferId, uint? ExpectedVersion, ReviseComponentsRequest Request) : ICommand<VersionedOffer>;

internal sealed class ReviseComponentsCommandValidator : AbstractValidator<ReviseComponentsCommand>
{
    public ReviseComponentsCommandValidator()
    {
        RuleFor(c => c.Request).NotNull();
        RuleFor(c => c.Request.Fixed).NotNull().LessThan(OfferLimits.MaxAmount).PrecisionScale(9, 1, true).OverridePropertyName("fixed");
        RuleFor(c => c.Request.Variable).NotNull().LessThan(OfferLimits.MaxAmount).PrecisionScale(9, 1, true).OverridePropertyName("variable");
        RuleFor(c => c.Request.Benefits).NotNull().LessThan(OfferLimits.MaxAmount).PrecisionScale(9, 1, true).OverridePropertyName("benefits");
        RuleFor(c => c.Request.Reason).MaximumLength(OfferLimits.LongText).OverridePropertyName("reason");
    }
}

internal sealed partial class ReviseComponentsCommandHandler(
    OfferWriter writer,
    IOfferRulesSource rulesSource,
    IWorkflowClient workflows,
    ILogger<ReviseComponentsCommandHandler> logger) : ICommandHandler<ReviseComponentsCommand, VersionedOffer>
{
    public async Task<Result<VersionedOffer>> Handle(ReviseComponentsCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var components = new CtcComponents(request.Fixed!.Value, request.Variable!.Value, request.Benefits!.Value);
        var rules = await rulesSource.GetAsync(ct);
        var violations = rules.CtcRules.Validate(components);
        if (violations.Count > 0)
        {
            return OfferErrors.CtcRulesBroken(violations);
        }

        Guid? voided = null;
        var result = await writer.WriteAsync(
            command.OfferId,
            command.ExpectedVersion,
            offer =>
            {
                var open = offer.State == OfferState.PendingApproval ? offer.WorkflowInstanceId : null;
                var revised = offer.Revise(components, request.Reason, writer.Actor, writer.Now);
                if (revised.IsSuccess && offer.State == OfferState.Draft)
                {
                    voided = open;
                }

                return revised;
            },
            ct);

        if (result.IsSuccess && voided is { } instanceId)
        {
            // The revision is saved; the old approval task must not be decided any more.
            try
            {
                await workflows.CancelAsync(instanceId, "The offer CTC was revised; it needs a new approval.", ct);
            }
            catch (DependencyUnavailableException ex)
            {
                CancelFailed(logger, command.OfferId, instanceId, ex);
            }
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Offer {OfferId} was revised but workflow {InstanceId} could not be cancelled; its late decision will be ignored")]
    private static partial void CancelFailed(ILogger logger, Guid offerId, Guid instanceId, Exception ex);
}

/// <summary>Body of <c>POST /api/v1/offers/{id}/verbal</c>.</summary>
public sealed record VerbalOfferRequest(decimal? Value, string? Outcome, string? Note);

/// <summary>RCU-OFR-005: log a verbal offer {value, outcome, actor}; appended immutably to the trail.</summary>
public sealed record LogVerbalOfferCommand(Guid OfferId, VerbalOfferRequest Request) : ICommand<VersionedOffer>;

internal sealed class LogVerbalOfferCommandValidator : AbstractValidator<LogVerbalOfferCommand>
{
    public LogVerbalOfferCommandValidator()
    {
        RuleFor(c => c.Request).NotNull();
        RuleFor(c => c.Request.Value).NotNull().GreaterThan(0).LessThan(OfferLimits.MaxAmount).OverridePropertyName("value");
        RuleFor(c => c.Request.Outcome).NotEmpty().MaximumLength(OfferLimits.ShortText).OverridePropertyName("outcome");
        RuleFor(c => c.Request.Note).MaximumLength(OfferLimits.LongText).OverridePropertyName("note");
    }
}

internal sealed class LogVerbalOfferCommandHandler(OfferWriter writer) : ICommandHandler<LogVerbalOfferCommand, VersionedOffer>
{
    public Task<Result<VersionedOffer>> Handle(LogVerbalOfferCommand command, CancellationToken ct) =>
        writer.WriteAsync(
            command.OfferId,
            null,
            offer => offer.LogVerbal(command.Request.Value!.Value, command.Request.Outcome!.Trim(), command.Request.Note, writer.Actor, writer.Now),
            ct);
}
