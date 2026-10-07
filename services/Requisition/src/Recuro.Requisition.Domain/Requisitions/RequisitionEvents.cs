using Recuro.BuildingBlocks.Domain;

namespace Recuro.Requisition.Domain.Requisitions;

public sealed record RequisitionSubmitted(
    Guid RequisitionId,
    string ReqId,
    string Grade,
    bool OutOfBudget,
    Guid WorkflowInstanceId,
    string ConfigVersionId) : IDomainEvent;

public sealed record RequisitionApproved(Guid RequisitionId, string ReqId, DateOnly? TargetClosure, string ConfigVersionId) : IDomainEvent;

public sealed record RequisitionRejected(Guid RequisitionId, string ReqId, string Reason, string ConfigVersionId) : IDomainEvent;

public sealed record RequisitionCancelled(Guid RequisitionId, string ReqId, RequisitionState From, string Reason) : IDomainEvent;
