using Recuro.BuildingBlocks.Domain;

namespace Recuro.BuildingBlocks.UnitTests;

public class TransitionTableTests
{
    private enum Light
    {
        Red,
        Green,
        Amber,
    }

    private static readonly TransitionTable<Light> Table = new(new Dictionary<Light, Light[]>
    {
        [Light.Red] = [Light.Green],
        [Light.Green] = [Light.Amber],
        [Light.Amber] = [Light.Red],
    });

    [Fact]
    public void Legal_move_succeeds()
    {
        Assert.True(Table.EnsureCanMove(Light.Red, Light.Green, "Light").IsSuccess);
    }

    [Fact]
    public void Illegal_move_is_a_conflict_that_lists_the_legal_targets()
    {
        var result = Table.EnsureCanMove(Light.Red, Light.Amber, "Light");

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Contains("Allowed: Green", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_must_cover_every_state()
    {
        Assert.Throws<ArgumentException>(() => new TransitionTable<Light>(new Dictionary<Light, Light[]>
        {
            [Light.Red] = [Light.Green],
        }));
    }
}
