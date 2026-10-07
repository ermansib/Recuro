using System.Reflection;
using Recuro.Audit.Domain.Entries;

namespace Recuro.Audit.UnitTests;

public class AuditChainTests
{
    private static readonly Guid Tenant = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 30, 15, TimeSpan.Zero);

    private static AuditRecord Record(string action) =>
        new("u-1", "A. Sharma", "hrta", "Requisition/REQ-2026-0156", action, "Draft", "PendingApproval", null, "v1", null, "corr-1", null, "/services/requisition", Now);

    private static List<AuditEntry> Chain(int length)
    {
        var entries = new List<AuditEntry>();
        for (var i = 0; i < length; i++)
        {
            entries.Add(AuditEntry.Append(Tenant, entries.LastOrDefault(), Record($"ACTION_{i}"), Now.AddSeconds(i)));
        }

        return entries;
    }

    [Fact]
    public void Entries_link_to_the_previous_hash_and_number_from_one()
    {
        var chain = Chain(3);

        Assert.Equal([1L, 2L, 3L], chain.Select(e => e.Sequence));
        Assert.Equal(AuditChain.GenesisHash, chain[0].PreviousHash);
        Assert.Equal(chain[0].Hash, chain[1].PreviousHash);
        Assert.Equal(chain[1].Hash, chain[2].PreviousHash);
    }

    [Fact]
    public void An_intact_chain_verifies()
    {
        var result = AuditChain.Verify(Chain(5), 0, AuditChain.GenesisHash);

        Assert.True(result.IsValid);
        Assert.Equal(5, result.CheckedEntries);
    }

    [Fact]
    public void Editing_any_field_breaks_the_chain_at_that_entry()
    {
        var chain = Chain(4);
        typeof(AuditEntry).GetProperty(nameof(AuditEntry.Reason))!.SetValue(chain[2], "edited later");

        var result = AuditChain.Verify(chain, 0, AuditChain.GenesisHash);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.BrokenAtSequence);
    }

    [Fact]
    public void Removing_an_entry_breaks_the_chain()
    {
        var chain = Chain(4);
        chain.RemoveAt(1);

        // The entry after the gap no longer links up.
        Assert.Equal(3, AuditChain.Verify(chain, 0, AuditChain.GenesisHash).BrokenAtSequence);
    }

    [Fact]
    public void Verification_can_start_from_a_seal()
    {
        var chain = Chain(4);

        var result = AuditChain.Verify(chain.Skip(2), chain[1].Sequence, chain[1].Hash);

        Assert.True(result.IsValid);
        Assert.Equal(chain[3].Hash, result.LastHash);
    }

    [Fact]
    public void Timestamps_are_truncated_to_database_precision_before_hashing()
    {
        var entry = AuditEntry.Append(Tenant, null, Record("X"), Now.AddTicks(7));

        Assert.Equal(0, entry.RecordedAt.Ticks % 10);
    }

    [Fact]
    public void An_entry_cannot_follow_another_tenants_entry()
    {
        var other = AuditEntry.Append(Guid.NewGuid(), null, Record("X"), Now);

        Assert.Throws<ArgumentException>(() => AuditEntry.Append(Tenant, other, Record("Y"), Now));
    }

    [Fact]
    public void Entries_expose_no_public_setters()
    {
        var setters = typeof(AuditEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod?.IsPublic == true)
            .Select(p => p.Name);

        Assert.Empty(setters);
    }
}
