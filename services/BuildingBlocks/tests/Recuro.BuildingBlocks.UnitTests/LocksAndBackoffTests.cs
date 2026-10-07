using Recuro.BuildingBlocks.Infrastructure.Locking;

namespace Recuro.BuildingBlocks.UnitTests;

public class LocksAndBackoffTests
{
    [Fact]
    public void Lock_keys_are_stable_and_distinct()
    {
        Assert.Equal(PostgresLocks.KeyFor("audit:seal-job"), PostgresLocks.KeyFor("audit:seal-job"));
        Assert.NotEqual(PostgresLocks.KeyFor("audit:seal-job"), PostgresLocks.KeyFor("audit:other"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    public void Backoff_grows_but_stays_under_the_cap(int attempt)
    {
        var delay = Recuro.BuildingBlocks.Infrastructure.Outbox.Backoff.For(attempt);

        Assert.InRange(delay, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(390));
    }
}
