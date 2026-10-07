using Recuro.Audit.Application.Entries;
using Recuro.Audit.Domain.Entries;

namespace Recuro.Audit.Application.Abstractions;

/// <summary>Appends to the current tenant's chain. Serialises appends per tenant so the chain never forks.</summary>
public interface IAuditChainWriter
{
    Task<AuditEntry> AppendAsync(AuditRecord record, CancellationToken ct);
}

/// <summary>Read side for the current tenant. Never returns another tenant's rows.</summary>
public interface IAuditReadStore
{
    Task<IReadOnlyList<AuditEventDto>> ListAsync(AuditFilter filter, CancellationToken ct);

    Task<AuditSeal?> GetLastSealAsync(CancellationToken ct);

    /// <summary>Entries after <paramref name="afterSequence"/>, in chain order.</summary>
    IAsyncEnumerable<AuditEntry> StreamChainAsync(long afterSequence, CancellationToken ct);
}

/// <summary>Filters for the audit list (RCU-AUD-003 adds export on top of the same filters).</summary>
public sealed record AuditFilter(
    string? Entity,
    string? ActorId,
    string? CorrelationId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit);
