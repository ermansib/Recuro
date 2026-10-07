using Recuro.BuildingBlocks.Domain;

namespace Recuro.Workflow.Domain.Workflows;

public static class WorkflowErrors
{
    /// <summary>RCU-WFL-002: a rejection needs a documented reason of at least this many characters.</summary>
    public const int MinReasonLength = 10;

    public static Error InstanceNotFound(Guid id) => Error.NotFound("workflow_not_found", $"Workflow {id} not found.");

    public static Error TaskNotFound(Guid id) => Error.NotFound("approval_not_found", $"Approval item {id} not found.");

    public static Error NotAssignee => Error.Forbidden("not_assignee", "This item is not in your queue.");

    public static Error AlreadyDecided => Error.Conflict("already_decided", "Already decided. Decisions are final.");

    public static Error NotActive(WorkflowStatus status) => Error.Conflict("workflow_not_active", $"The workflow is {status}.");

    public static Error UnknownAction(string actionId) =>
        Error.Validation([new FieldError("actionId", "unknown_action", $"Unknown action '{actionId}'.")]);

    public static Error ReasonRequired =>
        Error.Validation([new FieldError("reason", "reason_required", $"A documented reason of at least {MinReasonLength} characters is mandatory.")]);

    public static Error NotPaused => Error.Conflict("not_paused", "There is no open query on this item.");

    public static Error EmptyRoute => Error.Validation([new FieldError("legs", "required", "A route needs at least one leg with at least one assignee.")]);
}
