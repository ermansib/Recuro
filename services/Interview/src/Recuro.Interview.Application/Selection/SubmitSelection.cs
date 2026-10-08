using System.Globalization;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Domain.Rules;
using Recuro.Interview.Domain.Selection;

namespace Recuro.Interview.Application.Selection;

/// <summary>
/// RCU-INT-005/006: HR-TA submits the consolidated selection once every round has its feedback. Grades
/// the rules mark for ratification open an HR Head task in the shared workflow; the others are ratified at
/// once. Either way <c>interview.selection.ratified</c> moves the application on in Pipeline.
/// </summary>
public sealed record SubmitSelectionCommand(string AppId) : ICommand<SelectionSummaryDto>;

internal sealed partial class SubmitSelectionCommandHandler(
    IInterviewRepository interviews,
    ISelectionRepository selections,
    IInterviewRulesSource rulesSource,
    IWorkflowClient workflows,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<SubmitSelectionCommandHandler> logger) : ICommandHandler<SubmitSelectionCommand, SelectionSummaryDto>
{
    public const string WorkflowType = "SelectionRatification";
    public const string SubjectType = "Application";

    public async Task<Result<SelectionSummaryDto>> Handle(SubmitSelectionCommand command, CancellationToken ct)
    {
        var all = await interviews.ListForApplicationAsync(command.AppId, ct);
        var rounds = all.Where(r => r.Status != Domain.Interviews.InterviewStatus.Cancelled).OrderBy(r => r.RoundNumber).ToList();
        if (!SelectionSummaryBuilder.IsComplete(rounds))
        {
            return Domain.Interviews.InterviewErrors.SelectionIncomplete(command.AppId);
        }

        var rules = await rulesSource.GetAsync(ct);
        var last = rounds[^1];
        var summary = SelectionSummaryBuilder.Build(command.AppId, rounds, null);
        var input = new SelectionInput(
            command.AppId,
            last.ReqId,
            last.Grade,
            summary.OverallAverage,
            rounds.Count,
            rules.NeedsRatification(last.Grade),
            rules.ConfigVersionId);

        var by = user.Name ?? user.UserId ?? string.Empty;
        var now = clock.GetUtcNow();
        var decision = await selections.GetByAppIdAsync(command.AppId, ct);
        if (decision is null)
        {
            decision = SelectionDecision.Start(input, by, now);
            selections.Add(decision);
        }
        else
        {
            var resubmitted = decision.Resubmit(input, by, now);
            if (resubmitted.IsFailure)
            {
                return resubmitted.Error!;
            }
        }

        if (decision.RatificationRequired)
        {
            var instanceId = await workflows.StartAsync(BuildWorkflow(decision, rules.Ratification), ct);
            decision.AwaitRatification(instanceId);
            try
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await CompensateAsync(decision, instanceId, ct);
                throw;
            }
        }
        else
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return SelectionSummaryBuilder.Build(command.AppId, all, decision);
    }

    private async Task CompensateAsync(SelectionDecision decision, Guid instanceId, CancellationToken ct)
    {
        Compensating(logger, decision.AppId, instanceId);
        try
        {
            await workflows.CancelAsync(instanceId, "Selection submit did not complete.", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CompensationFailed(logger, decision.AppId, instanceId, ex);
        }
    }

    private static WorkflowStart BuildWorkflow(SelectionDecision decision, RatificationRule rule)
    {
        var average = decision.OverallAverage?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—";
        return new WorkflowStart(
            WorkflowType,
            new WorkflowSubject(SubjectType, decision.AppId),
            decision.ConfigVersionId,
            string.Create(CultureInfo.InvariantCulture, $"selection:{decision.Id:N}:{decision.SubmittedAt.ToUnixTimeMilliseconds()}"),
            [new WorkflowLeg("Ratification", [new WorkflowAssignee(rule.Role, rule.Label)], rule.SlaWorkingDays, [])],
            new WorkflowPresentation(
                "Task",
                "task",
                $"Selection Ratification — {decision.AppId}",
                string.Create(CultureInfo.InvariantCulture, $"{decision.ReqId} · {decision.Grade} · {decision.Rounds} round(s) · panel avg {average}/5"),
                $"Route: HR-TA → {rule.Label} (ratification, RCU-INT-006)",
                new WorkflowChip("Selection", "navy"),
                [
                    new WorkflowAction("approve", "Ratify", "primary", "resolve", "Selection ratified"),
                    new WorkflowAction("reject", "Return to TA", "danger", "reject", null),
                ]));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Selection submit for {AppId} did not complete; cancelling workflow {InstanceId} (compensation)")]
    private static partial void Compensating(ILogger logger, string appId, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Compensation failed: workflow {InstanceId} for {AppId} is still open and needs an operator")]
    private static partial void CompensationFailed(ILogger logger, string appId, Guid instanceId, Exception ex);
}

/// <summary>The fields of <c>workflow.task.completed.v1</c> this service reads (tolerant reader).</summary>
public sealed record WorkflowTaskCompletedPayload(Guid? InstanceId, string? Type, string? SubjectType, string? SubjectId, string? Reason, string? InstanceStatus);

/// <summary>RCU-INT-006: the ratification workflow finished. Approved → ratified (event published); rejected → back to HR-TA.</summary>
public sealed partial class RatificationCompletedHandler(
    ISelectionRepository selections,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<RatificationCompletedHandler> logger) : IIntegrationEventHandler<WorkflowTaskCompletedPayload>
{
    public async Task Handle(IntegrationEvent<WorkflowTaskCompletedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        if (data.Type != SubmitSelectionCommandHandler.WorkflowType || data.InstanceId is null || data.InstanceStatus is not ("Approved" or "Rejected"))
        {
            return;
        }

        var decision = await selections.GetByWorkflowAsync(data.InstanceId.Value, ct);
        if (decision is null)
        {
            // A superseded instance (resubmitted since) or another tenant's: nothing to do.
            Unknown(logger, data.InstanceId.Value, integrationEvent.Metadata.Id);
            return;
        }

        var applied = decision.ApplyRatification(data.InstanceStatus == "Approved", integrationEvent.Metadata.ActorName, data.Reason, clock.GetUtcNow());
        if (applied.IsFailure)
        {
            Ignored(logger, decision.AppId, decision.Status, applied.Error!.Message);
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ratification completed for unknown workflow {InstanceId} (event {EventId})")]
    private static partial void Unknown(ILogger logger, Guid instanceId, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ratification outcome for {AppId} ignored in state {Status}: {Reason}")]
    private static partial void Ignored(ILogger logger, string appId, SelectionStatus status, string reason);
}
