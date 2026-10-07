using Recuro.BuildingBlocks.Domain;

namespace Recuro.Requisition.Domain.Requisitions;

public static class RequisitionErrors
{
    public static Error NotFound(string reqId) => Error.NotFound("requisition_not_found", $"Requisition {reqId} not found.");

    public static Error NotADraft(string reqId, RequisitionState state) =>
        Error.Conflict("requisition_not_draft", $"{reqId} is {state}; only drafts can be edited or submitted.");

    public static Error StaleVersion(string reqId) =>
        Error.Conflict("stale_version", $"{reqId} was changed by someone else. Reload it and try again.");

    public static Error Incomplete(IReadOnlyList<(string Field, string Code, string Message)> missing) =>
        Error.Validation(missing.Select(m => new FieldError(m.Field, m.Code, m.Message)).ToList());

    public static Error SourcingLocked(string reqId, RequisitionState state) =>
        Error.Conflict("sourcing_locked", $"Sourcing is locked: {reqId} is {state}, not Approved.");

    public static Error ReasonRequired(string field) =>
        Error.Validation([new FieldError(field, "reason_required", $"A documented reason of at least {RequisitionLimits.MinReasonLength} characters is mandatory.")]);
}
