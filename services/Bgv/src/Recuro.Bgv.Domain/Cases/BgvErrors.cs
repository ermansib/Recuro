using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Domain.Cases;

public static class BgvErrors
{
    public static Error NotFound(string reference) => Error.NotFound("bgv_case_not_found", $"No BGV case for {reference}.");

    public static Error CheckNotFound(string type) => Error.NotFound("bgv_check_not_found", $"Check {type} is not on this case.");

    public static readonly Error ConsentRequired = Error.Validation(
        [new FieldError("consent", "consent_required", "Record the candidate's BGV consent before initiating verification.")]);

    public static Error VendorNotActive(string vendorId) =>
        Error.Validation([new FieldError("vendorId", "vendor_not_active", $"Vendor {vendorId} is not an active, empanelled BGV agency.")]);

    public static Error NotAtBgvStage(string appId) =>
        Error.Conflict("application_not_at_bgv", $"Application {appId} is not at the BGV stage.");

    public static Error AlreadyInitiated(string appId) =>
        Error.Conflict("bgv_case_exists", $"Application {appId} already has a BGV case.");

    public static readonly Error UseAdverseReport = Error.Validation(
        [new FieldError("status", "use_adverse_report", "Report an adverse finding to flag a check; it needs a description and an escalation.")]);

    public static readonly Error DescriptionRequired = Error.Validation(
        [new FieldError("description", "description_required", "Describe the finding.")]);

    public static readonly Error ReasonRequired = Error.Validation(
        [new FieldError("reason", "reason_required", "The decision on an adverse finding must be documented.")]);

    public static readonly Error AdverseAlreadyOpen =
        Error.Conflict("adverse_open", "An adverse finding on this case is already under review.");

    public static readonly Error UnderReview =
        Error.Conflict("adverse_under_review", "The flagged check is under review; it clears only through the escalation decision.");

    public static readonly Error NothingToResolve =
        Error.Conflict("no_adverse_finding", "There is no adverse finding under review on this case.");

    public static readonly Error Rescinded =
        Error.Conflict("bgv_rescinded", "The adverse finding was upheld and the offer rescinded.");

    public static Error Closed(CaseStatus status) => Error.Conflict("bgv_case_closed", $"The case is {status} and can no longer change.");

    public static Error CannotFlag(string label, CheckStatus status) =>
        Error.Conflict("illegal_transition", $"{label} is {status} and cannot be flagged.");

    /// <summary>RCU-BGV-005: the block error enumerates every pending, in-progress or flagged check.</summary>
    public static Error ReleaseBlocked(CaseStatus status, IReadOnlyList<GateBlocker> blockers)
    {
        ArgumentNullException.ThrowIfNull(blockers);
        var count = (CheckStatus s) => blockers.Count(b => b.Status == s);
        var message = status == CaseStatus.UnderReview && blockers.Count == 0
            ? "Cannot release — an adverse finding is under review."
            : $"Cannot release — {count(CheckStatus.InProgress)} in progress, {count(CheckStatus.Pending)} pending, {count(CheckStatus.Flagged)} flagged: {string.Join(", ", blockers.Select(b => b.Label))}";
        return Error.Conflict("bgv_release_blocked", message) with
        {
            Fields = blockers.Select(b => new FieldError(b.Type, b.Status.ToString(), b.Label)).ToList(),
        };
    }
}
