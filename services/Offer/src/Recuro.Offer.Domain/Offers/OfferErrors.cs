using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Domain.Rules;

namespace Recuro.Offer.Domain.Offers;

public static class OfferErrors
{
    public static Error NotFound(Guid id) => Error.NotFound("offer_not_found", $"Offer {id} not found.");

    public static Error NotEditable(OfferState state) =>
        Error.Conflict("offer_not_editable", $"Offer is {state} and can no longer be edited.");

    public static Error AlreadyExists(string appId) =>
        Error.Conflict("offer_exists", $"{appId} already has an open offer. Withdraw it before drafting another.");

    public static Error NotInOfferStage(string appId, string stage) =>
        Error.Conflict("application_not_in_offer", $"{appId} is at {stage}, not Offer.");

    public static Error ReasonRequired(string field = "reason") =>
        Error.Validation([new FieldError(field, "reason_required", $"A documented reason of at least {OfferLimits.MinReasonLength} characters is mandatory.")]);

    public static Error CtcRulesBroken(IEnumerable<CtcViolation> violations) =>
        Error.Validation(violations.Select(v => new FieldError(v.Field, v.RuleId, v.Message)).ToList());

    public static Error NoApprovalRule(string grade) =>
        Error.Validation([new FieldError("grade", "no_offer_rule", $"The offer matrix has no approval rule for grade {grade}.")]);

    public static Error ReleaseBlocked(IReadOnlyList<string> blockers) =>
        Error.Conflict("release_blocked", $"Cannot release — BGV checks not cleared: {string.Join(", ", blockers)}.");

    public static Error StaleVersion(Guid id) =>
        Error.Conflict("stale_version", $"Offer {id} was changed by someone else. Reload it and try again.");

    public static Error NotPendingApproval(Guid id, OfferState state) =>
        Error.Conflict("offer_not_pending_approval", $"Offer {id} is {state}, not waiting for approval.");

    public static Error NoOpenApproval(Guid id) =>
        Error.Conflict("no_open_approval", $"Offer {id} has no approval task waiting for a decision.");
}
