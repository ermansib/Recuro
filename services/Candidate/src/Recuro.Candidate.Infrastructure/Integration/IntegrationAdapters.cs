using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Candidate.Application.Abstractions;

namespace Recuro.Candidate.Infrastructure.Integration;

/// <summary>
/// Stand-in until the Vendor service (wave 2) publishes its vendor status contract: every consultant id
/// is accepted and the gap is logged. Swap in an HTTP or event-built implementation then.
/// </summary>
internal sealed partial class UncheckedVendorDirectory(ILogger<UncheckedVendorDirectory> logger) : IVendorDirectory
{
    public Task<bool> IsActiveConsultantAsync(string consultantId, CancellationToken ct)
    {
        NotChecked(logger, consultantId);
        return Task.FromResult(true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consultant {ConsultantId} accepted without a vendor check: the Vendor service is not connected yet")]
    private static partial void NotChecked(ILogger logger, string consultantId);
}

/// <summary>
/// RCU-CND-004 read audit as structured log lines: who read which candidate ids, never the data itself.
/// Logs are retained per BNFR-6.
/// </summary>
internal sealed partial class LoggingPersonalDataAccessLog(ICurrentUser caller, ILogger<LoggingPersonalDataAccessLog> logger) : IPersonalDataAccessLog
{
    public void Read(IReadOnlyCollection<Guid> candidateIds, string purpose) =>
        PersonalDataRead(logger, caller.UserId, caller.Roles, purpose, candidateIds);

    [LoggerMessage(Level = LogLevel.Information, Message = "PII read by {ActorId} ({ActorRoles}) for {Purpose}: {CandidateIds}")]
    private static partial void PersonalDataRead(ILogger logger, string? actorId, IReadOnlyCollection<string> actorRoles, string purpose, IReadOnlyCollection<Guid> candidateIds);
}
