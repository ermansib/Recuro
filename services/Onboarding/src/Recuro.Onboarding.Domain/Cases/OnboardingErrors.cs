using Recuro.BuildingBlocks.Domain;

namespace Recuro.Onboarding.Domain.Cases;

public static class OnboardingErrors
{
    public static Error NotFound(string reference) => Error.NotFound("onboarding_case_not_found", $"No onboarding case for {reference}.");

    public static Error ItemNotFound(string key) => Error.NotFound("checklist_item_not_found", $"Checklist item {key} is not on this case.");

    public static Error DocumentNotFound(string type) => Error.NotFound("document_not_found", $"Document {type} is not on this case's list.");

    public static Error MilestoneNotFound(Guid id) => Error.NotFound("milestone_not_found", $"Milestone {id} is not on this case.");

    public static Error Closed(CaseStatus status) => Error.Conflict("onboarding_case_closed", $"The case is {status} and can no longer change.");

    public static readonly Error ChecklistLocked = Error.Conflict(
        "checklist_locked",
        "The record is Day-1 Ready and Finance & Payroll were notified; the checklist is locked.");

    public static readonly Error NothingToReview = Error.Conflict(
        "document_not_uploaded",
        "Upload the document before verifying it.");

    public static readonly Error RejectionNoteRequired = Error.Validation(
        [new FieldError("note", "note_required", "Say why the document is rejected so the new hire can fix it.")]);

    public static readonly Error UseProbationDecision = Error.Conflict(
        "use_probation_decision",
        "The end of probation is completed by a confirm or extend decision.");

    public static Error FileIncomplete(IReadOnlyList<MissingDocument> missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        return Error.Conflict(
            "file_incomplete",
            $"The file is incomplete — {missing.Count} mandatory document(s) not verified: {string.Join(", ", missing.Select(m => m.Label))}") with
        {
            Fields = missing.Select(m => new FieldError(m.Type, m.Status.ToString(), m.Label)).ToList(),
        };
    }

    public static readonly Error NotJoined = Error.Conflict(
        "not_day1_ready",
        "Probation decisions need a Day-1 Ready record.");

    public static Error ProbationNotEnded(DateOnly endsOn) =>
        Error.Conflict("probation_not_ended", FormattableString.Invariant($"Probation ends on {endsOn:yyyy-MM-dd}; decide on or after that date."));

    public static Error BgvNotCleared(BgvStatus status) =>
        Error.Conflict("bgv_not_cleared", $"Confirmation waits for background verification to clear (now {status}).");

    public static readonly Error ExtensionLength = Error.Validation(
        [new FieldError("extendByMonths", "extension_length", "Extend probation by 1 to 6 months.")]);

    public static readonly Error ReasonRequired = Error.Validation(
        [new FieldError("reason", "reason_required", "An extension must give its reason.")]);
}

/// <summary>A mandatory §13 document that keeps the file from being complete.</summary>
public sealed record MissingDocument(string Type, string Label, DocumentStatus Status);
