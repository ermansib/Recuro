using Recuro.BuildingBlocks.Domain;

namespace Recuro.Candidate.Domain.Candidates;

public static class CandidateErrors
{
    public static Error NotFound(Guid id) => Error.NotFound("candidate_not_found", $"Candidate {id} was not found.");

    public static Error MissingConsent(IEnumerable<ConsentType> missing) =>
        Error.Validation([new FieldError("consents", "consent_required", $"Required consent missing: {string.Join(", ", missing)}.")]);

    public static Error Duplicate(Guid existingId) =>
        Error.Conflict("duplicate_candidate", $"A candidate with the same email or phone already exists ({existingId}).");

    public static readonly Error ConsultantNotActive =
        Error.Validation([new FieldError("consultantId", "vendor_not_active", "The consultant must be an active empanelled vendor.")]);

    public static readonly Error OnLegalHold = Error.Conflict("legal_hold", "The candidate is on legal hold and cannot be purged.");

    public static readonly Error AlreadyPurged = Error.Conflict("candidate_purged", "The candidate's personal data has been purged.");

    public static readonly Error NotDueForPurge = Error.Conflict("not_due_for_purge", "The candidate is not past the retention period.");
}
