using Microsoft.Extensions.Logging;
using Recuro.Bgv.Application.Abstractions;
using Recuro.Bgv.Application.Cases.Commands;
using Recuro.Bgv.Domain.Cases;
using Recuro.Bgv.Domain.Requests;
using Recuro.BuildingBlocks.Application.IntegrationEvents;

namespace Recuro.Bgv.Application.Cases.Events;

// Each consumer declares only the fields it reads (tolerant reader), per the event catalog (RCU-BKD-001 §5).
// The inbox processor commits each handler's changes with its inbox row.

/// <summary><c>pipeline.stage.changed.v1</c>.</summary>
public sealed record StageChangedPayload(string AppId, string? ReqId, string? CandidateId, string? Source, string? From, string To, DateTimeOffset? At);

/// <summary><c>vendor.de_empanelled.v1</c>.</summary>
public sealed record VendorDeEmpanelledPayload(string VendorId);

/// <summary>The fields of <c>workflow.task.completed.v1</c> this service reads.</summary>
public sealed record WorkflowTaskCompletedPayload(
    string? Type,
    string? SubjectType,
    string? SubjectId,
    string? ActionId,
    string? Reason,
    string? InstanceStatus);

/// <summary>
/// Keeps the local list of applications at the BGV stage (the initiation precondition) and cancels a
/// running case when its application is rejected or withdrawn.
/// </summary>
public sealed class StageChangedHandler(IBgvRequestRepository requests, IBgvCaseRepository cases)
    : IIntegrationEventHandler<StageChangedPayload>
{
    private const string Bgv = "BGV";
    private const string Hold = "Hold";

    public async Task Handle(IntegrationEvent<StageChangedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        var at = data.At ?? integrationEvent.Metadata.Time;

        // On hold from BGV (e.g. an adverse finding) still counts as at BGV.
        var atBgv = data.To == Bgv || (data.To == Hold && data.From == Bgv);
        var request = await requests.GetAsync(data.AppId, ct);
        if (request is null)
        {
            if (data.To == Bgv)
            {
                requests.Add(BgvRequest.Arrived(data.AppId, data.ReqId ?? string.Empty, data.CandidateId ?? string.Empty, data.Source ?? string.Empty, at));
            }
        }
        else
        {
            request.Update(atBgv, at);
        }

        if (data.To is "Rejected" or "Withdrawn")
        {
            (await cases.FindAsync(data.AppId, ct))?.Cancel();
        }
    }
}

/// <summary>RCU-VND-003: a de-empanelled vendor's open cases wait for reassignment to an active vendor.</summary>
public sealed class VendorDeEmpanelledHandler(IBgvCaseRepository cases) : IIntegrationEventHandler<VendorDeEmpanelledPayload>
{
    public async Task Handle(IntegrationEvent<VendorDeEmpanelledPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        foreach (var bgvCase in await cases.ListOpenByVendorAsync(integrationEvent.Data.VendorId, ct))
        {
            bgvCase.FlagForReassignment();
        }
    }
}

/// <summary>
/// RCU-BGV-006: the adverse escalation finished in the approvals inbox. "Accept — Rescind" closes the case
/// adverse; "Override — Proceed" clears the flagged check with the documented rationale.
/// </summary>
public sealed partial class AdverseDecisionHandler(
    IBgvCaseRepository cases,
    TimeProvider clock,
    ILogger<AdverseDecisionHandler> logger) : IIntegrationEventHandler<WorkflowTaskCompletedPayload>
{
    public async Task Handle(IntegrationEvent<WorkflowTaskCompletedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        if (data.Type != AdverseEscalation.WorkflowType || data.SubjectType != AdverseEscalation.SubjectType || string.IsNullOrEmpty(data.SubjectId))
        {
            return;
        }

        AdverseOutcome? outcome = data.ActionId switch
        {
            AdverseEscalation.RescindAction => AdverseOutcome.Rescind,
            AdverseEscalation.OverrideAction => AdverseOutcome.Override,
            _ => null,
        };
        if (outcome is null)
        {
            // An escalation step (HR Head → MD/CEO), not a decision.
            return;
        }

        var bgvCase = await cases.FindAsync(data.SubjectId, ct);
        if (bgvCase is null)
        {
            UnknownCase(logger, data.SubjectId, integrationEvent.Metadata.Id);
            return;
        }

        var resolved = bgvCase.Resolve(outcome.Value, data.Reason ?? string.Empty, Actors.FromEvent(integrationEvent.Metadata), clock.GetUtcNow());
        if (resolved.IsFailure)
        {
            // Already decided, or the case closed meanwhile. Nothing to retry.
            DecisionIgnored(logger, data.SubjectId, resolved.Error!.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Adverse BGV decision for unknown case {CaseId} (event {EventId})")]
    private static partial void UnknownCase(ILogger logger, string caseId, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Adverse BGV decision for case {CaseId} ignored: {Reason}")]
    private static partial void DecisionIgnored(ILogger logger, string caseId, string reason);
}
