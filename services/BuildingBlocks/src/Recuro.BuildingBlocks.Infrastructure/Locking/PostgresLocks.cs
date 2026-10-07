using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Recuro.BuildingBlocks.Infrastructure.Locking;

/// <summary>
/// Transaction-scoped PostgreSQL advisory locks. Use them for schedulers that must not double-fire
/// across replicas (BNFR-4) and for short critical sections such as appending to a hash chain.
/// The lock is released when the surrounding transaction ends.
/// </summary>
public static class PostgresLocks
{
    /// <summary>Waits for the lock. Requires an open transaction.</summary>
    public static Task LockAsync(DbContext db, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var key = KeyFor(name);
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }

    /// <summary>Takes the lock if free. False means another instance holds it: skip this run.</summary>
    public static async Task<bool> TryLockAsync(DbContext db, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var key = KeyFor(name);
        return await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({key}) AS \"Value\"")
            .SingleAsync(ct);
    }

    /// <summary>Stable 64-bit key from a lock name.</summary>
    public static long KeyFor(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(name), hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
