using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Recuro.Audit.Domain.Entries;

/// <summary>Hashing and verification of a tenant's audit chain (SHA-256 over a canonical JSON array).</summary>
public static class AuditChain
{
    /// <summary>The "previous hash" of a tenant's first entry.</summary>
    public static readonly string GenesisHash = new('0', 64);

    public static string ComputeHash(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // A JSON array keeps field order fixed and escapes separators, so the input is unambiguous.
        var canonical = JsonSerializer.Serialize(new object?[]
        {
            entry.PreviousHash,
            entry.TenantId,
            entry.Sequence,
            entry.ActorId,
            entry.ActorName,
            entry.ActorRole,
            entry.Entity,
            entry.Action,
            entry.Before,
            entry.After,
            entry.Reason,
            entry.ConfigVersion,
            entry.IpAddress,
            entry.CorrelationId,
            entry.SourceEventId,
            entry.Source,
            entry.OccurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            entry.RecordedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>
    /// Checks consecutive entries (ordered by sequence) starting after a known point: the genesis or the
    /// last seal. Returns the first sequence whose link or content does not match.
    /// </summary>
    public static ChainVerification Verify(IEnumerable<AuditEntry> orderedEntries, long afterSequence, string afterHash)
    {
        ArgumentNullException.ThrowIfNull(orderedEntries);
        var expectedSequence = afterSequence + 1;
        var expectedPrevious = afterHash;
        long checkedCount = 0;

        foreach (var entry in orderedEntries)
        {
            if (entry.Sequence != expectedSequence || entry.PreviousHash != expectedPrevious || entry.Hash != ComputeHash(entry))
            {
                return new ChainVerification(false, checkedCount, entry.Sequence, expectedPrevious);
            }

            checkedCount++;
            expectedSequence++;
            expectedPrevious = entry.Hash;
        }

        return new ChainVerification(true, checkedCount, null, expectedPrevious);
    }

    /// <summary>Truncates to microseconds, the precision of PostgreSQL <c>timestamptz</c>.</summary>
    public static DateTimeOffset ToStoredPrecision(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}

/// <summary>Result of verifying part of a chain. <see cref="LastHash"/> is the hash of the last good entry.</summary>
public sealed record ChainVerification(bool IsValid, long CheckedEntries, long? BrokenAtSequence, string LastHash);
