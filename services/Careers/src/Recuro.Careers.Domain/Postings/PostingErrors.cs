using Recuro.BuildingBlocks.Domain;

namespace Recuro.Careers.Domain.Postings;

public static class PostingErrors
{
    public static Error NotFound(string reqId) => Error.NotFound("posting_not_found", $"There is no posting for {reqId}.");

    public static Error Closed(string postingId) => Error.Conflict("posting_closed", $"Posting {postingId} is closed.");

    public static Error SourcingLocked(string reqId) =>
        Error.Conflict("sourcing_locked", $"{reqId} is not open for sourcing yet. Publish it once the requisition is approved.");

    public static readonly Error JustificationRequired =
        Error.Validation([new FieldError("justification", "required", "Opening the public site during the IJP window needs a justification.")]);

    public static readonly Error ReasonRequired =
        Error.Validation([new FieldError("reason", "required", "Say why the posting is taken down.")]);

    public static readonly Error EarlyReleaseNotAllowed =
        Error.Forbidden("early_release_forbidden", "Only the HR Head can open a posting before the IJP window ends.");
}
