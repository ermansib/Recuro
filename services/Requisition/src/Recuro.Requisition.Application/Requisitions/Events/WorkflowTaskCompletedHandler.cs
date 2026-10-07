using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Requisition.Application.Abstractions;

namespace Recuro.Requisition.Application.Requisitions.Events;

/// <summary>
/// The fields of <c>workflow.task.completed.v1</c> this service reads (tolerant reader: anything else in
/// the payload is ignored).
/// </summary>
public sealed record WorkflowTaskCompletedPayload(
    string? Type,
    string? SubjectType,
    string? SubjectId,
    string? Decision,
    string? Reason,
    string? InstanceStatus);

/// <summary>
/// RCU-REQ-006: when the MRF workflow finishes, the requisition moves to Approved (sourcing unlocks) or
/// Rejected. Task completions that don't finish the instance (an earlier leg) change nothing here.
/// </summary>
public sealed partial class WorkflowTaskCompletedHandler(
    IRequisitionRepository requisitions,
    TimeProvider clock,
    ILogger<WorkflowTaskCompletedHandler> logger) : IIntegrationEventHandler<WorkflowTaskCompletedPayload>
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    public async Task Handle(IntegrationEvent<WorkflowTaskCompletedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        if (data.Type != RequisitionSubmission.WorkflowType
            || data.SubjectType != RequisitionSubmission.SubjectType
            || string.IsNullOrEmpty(data.SubjectId)
            || data.InstanceStatus is not (Approved or Rejected))
        {
            return;
        }

        var requisition = await requisitions.GetByReqIdAsync(data.SubjectId, ct);
        if (requisition is null)
        {
            UnknownRequisition(logger, data.SubjectId, integrationEvent.Metadata.Id);
            return;
        }

        var result = requisition.ApplyApprovalOutcome(data.InstanceStatus == Approved, data.Reason, clock.GetUtcNow());
        if (result.IsFailure)
        {
            // E.g. cancelled while the approval was in flight. Nothing to retry: log and move on.
            OutcomeIgnored(logger, requisition.ReqId, requisition.State, result.Error!.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workflow completed for unknown requisition {ReqId} (event {EventId})")]
    private static partial void UnknownRequisition(ILogger logger, string reqId, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Approval outcome for {ReqId} ignored in state {State}: {Reason}")]
    private static partial void OutcomeIgnored(ILogger logger, string reqId, Domain.Requisitions.RequisitionState state, string reason);
}
