using Microsoft.Extensions.Logging;
using Recuro.Notification.Application.Abstractions;

namespace Recuro.Notification.Infrastructure.Directory;

/// <summary>
/// Stand-in until Candidate publishes a contact lookup (the candidate's email is Candidate's PII).
/// Returns no address, so candidate emails are logged as suppressed rather than lost silently.
/// </summary>
internal sealed partial class UnavailableCandidateContacts(ILogger<UnavailableCandidateContacts> logger) : ICandidateContacts
{
    public Task<CandidateContact?> FindAsync(string candidateId, CancellationToken ct)
    {
        NoContactSource(logger, candidateId);
        return Task.FromResult<CandidateContact?>(null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No candidate contact source yet; email to candidate {CandidateId} will be suppressed")]
    private static partial void NoContactSource(ILogger logger, string candidateId);
}
