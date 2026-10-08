using System.Globalization;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Offers.Commands;

/// <summary>
/// RCU-OFR-002/003 (frontend submitOffer): validate the CTC, resolve the Annexure D route from the
/// Config offer matrix and open one approval task for the resolved authority in the shared workflow.
/// </summary>
public sealed record SubmitOfferCommand(Guid OfferId, uint? ExpectedVersion) : ICommand<VersionedOffer>;

internal sealed partial class SubmitOfferCommandHandler(
    OfferWriter writer,
    IOfferRulesSource rulesSource,
    IWorkflowClient workflows,
    IUnitOfWork unitOfWork,
    ILogger<SubmitOfferCommandHandler> logger) : ICommandHandler<SubmitOfferCommand, VersionedOffer>
{
    public const string SubjectType = "Offer";
    public const string WithinBandKind = "Offer";
    public const string DeviationKind = "Deviation";

    /// <summary>Workflow types this service opens; completions of other types are not ours.</summary>
    public static readonly IReadOnlySet<string> WorkflowTypes = new HashSet<string>(StringComparer.Ordinal) { WithinBandKind, DeviationKind };

    /// <summary>Annexure D: the approver decides within two working days.</summary>
    private const int ApprovalSlaWorkingDays = 2;

    public async Task<Result<VersionedOffer>> Handle(SubmitOfferCommand command, CancellationToken ct)
    {
        var loaded = await writer.LoadAsync(command.OfferId, command.ExpectedVersion, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var offer = loaded.Value;
        var rules = await rulesSource.GetAsync(ct);
        var violations = rules.CtcRules.Validate(offer.Components);
        if (violations.Count > 0)
        {
            return OfferErrors.CtcRulesBroken(violations);
        }

        var route = rules.Route(offer.Grade, offer.Components, offer.Band);
        if (route is null)
        {
            return OfferErrors.NoApprovalRule(offer.Grade);
        }

        var submitted = offer.Submit(route, writer.Actor, writer.Now);
        if (submitted.IsFailure)
        {
            return submitted.Error!;
        }

        var instanceId = await workflows.StartAsync(BuildWorkflow(offer, route), ct);
        offer.AwaitApproval(instanceId);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await CompensateAsync(offer.Id, instanceId, ct);
            throw;
        }

        return await writer.ViewAsync(offer, ct);
    }

    private async Task CompensateAsync(Guid offerId, Guid instanceId, CancellationToken ct)
    {
        Compensating(logger, offerId, instanceId);
        try
        {
            await workflows.CancelAsync(instanceId, "Offer submit did not complete.", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CompensationFailed(logger, offerId, instanceId, ex);
        }
    }

    internal static WorkflowStart BuildWorkflow(JobOffer offer, OfferRoute route)
    {
        var total = route.Total.ToString("0.0", CultureInfo.InvariantCulture);
        var over = route.Deviation.ToString("0.0", CultureInfo.InvariantCulture);
        var band = string.Create(CultureInfo.InvariantCulture, $"₹{offer.Band.Min:0.##}–{offer.Band.Max:0.##}L");
        var kind = route.WithinBand ? WithinBandKind : DeviationKind;
        return new WorkflowStart(
            kind,
            new WorkflowSubject(SubjectType, offer.Id.ToString()),
            route.ConfigVersionId,
            string.Create(CultureInfo.InvariantCulture, $"offer:{offer.Id:N}:{offer.Trail.Count}"),
            [new WorkflowLeg("Approving", [new WorkflowAssignee(route.ApproverRole, ApproverLabel(route))], ApprovalSlaWorkingDays, [])],
            new WorkflowPresentation(
                kind,
                route.WithinBand ? "default" : "dev",
                $"{(route.WithinBand ? "Offer Sign-off (Within Band)" : "CTC Deviation")} — {offer.CandidateName}",
                $"{offer.Designation} · {offer.Grade} · band {band}",
                route.Label,
                new WorkflowSensitive("CTC", $"₹{total}L"),
                route.WithinBand ? new WorkflowChip("Annexure D", "navy") : new WorkflowChip($"+₹{over}L over band", "red"),
                [
                    new WorkflowAction("approve", route.WithinBand ? "Sign-off" : "Approve Deviation", "primary", "resolve", "Offer approved (Annexure D)"),
                    new WorkflowAction("decline", "Return to TA", "danger", "reject", null),
                ]));
    }

    /// <summary>"HR-TA → HR Head" names the approver after the arrow.</summary>
    private static string ApproverLabel(OfferRoute route)
    {
        var arrow = route.Label.LastIndexOf('→');
        return arrow >= 0 ? route.Label[(arrow + 1)..].Trim() : route.Label;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Offer {OfferId} submit did not complete; cancelling workflow {InstanceId} (compensation)")]
    private static partial void Compensating(ILogger logger, Guid offerId, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Compensation failed: workflow {InstanceId} for offer {OfferId} is still open and needs an operator")]
    private static partial void CompensationFailed(ILogger logger, Guid offerId, Guid instanceId, Exception ex);
}
