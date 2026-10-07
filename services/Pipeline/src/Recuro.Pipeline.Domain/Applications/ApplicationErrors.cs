using Recuro.BuildingBlocks.Domain;

namespace Recuro.Pipeline.Domain.Applications;

public static class ApplicationErrors
{
    public static Error NotFound(string appId) => Error.NotFound("application_not_found", $"Application {appId} was not found.");

    public static readonly Error UseReject = Error.Validation(
        [new FieldError("to", "use_reject", "Use reject with a documented reason to reject an application.")]);

    public static readonly Error ReasonRequired = Error.Validation(
        [new FieldError("reason", "reason_required", "A rejection reason is mandatory.")]);

    public static Error SourcingLocked(string reqId) =>
        Error.Conflict("sourcing_locked", $"Sourcing is locked: {reqId} is not approved for sourcing.");

    public static Error UnknownCandidate(string candidateId) =>
        Error.Validation([new FieldError("candidateId", "candidate_not_found", $"Candidate {candidateId} does not exist.")]);

    public static Error AlreadyApplied(string appId) =>
        Error.Conflict("already_applied", $"The candidate already has an active application for this requisition ({appId}).");
}
