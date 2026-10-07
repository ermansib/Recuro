namespace Recuro.Requisition.Domain.Requisitions;

/// <summary>The DOA route resolved by the Config service at submit (frontend <c>Requisition.route</c>).</summary>
public sealed record ApprovalRoute(string Initiating, string Recommending, string Approving, string ApproverRole)
{
    public static ApprovalRoute None { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
}

/// <summary>Everything submit learned from Config and Workflow, applied in one step.</summary>
/// <param name="Route">Display route for the tracker.</param>
/// <param name="ConfigVersionId">Rules version the requisition is pinned to (RCU-CFG-003).</param>
/// <param name="TargetClosure">Submit date + overall TAT in working days (RCU-REQ-004).</param>
/// <param name="WorkflowInstanceId">The approval workflow opened for it.</param>
public sealed record SubmissionPlan(ApprovalRoute Route, string ConfigVersionId, DateOnly TargetClosure, Guid WorkflowInstanceId);
