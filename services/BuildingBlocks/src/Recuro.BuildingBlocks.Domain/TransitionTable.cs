namespace Recuro.BuildingBlocks.Domain;

/// <summary>
/// Legal state transitions encoded as data (FRD §6), so the rules live in one table instead of
/// scattered <c>if</c>s. The frontend keeps the same tables in <c>frontend/src/domain/stateMachines.ts</c>.
/// </summary>
public sealed class TransitionTable<TState>
    where TState : struct, Enum
{
    private readonly IReadOnlyDictionary<TState, TState[]> _allowed;

    public TransitionTable(IReadOnlyDictionary<TState, TState[]> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        var missing = Enum.GetValues<TState>().Where(state => !allowed.ContainsKey(state)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"Transition table has no row for: {string.Join(", ", missing)}.", nameof(allowed));
        }

        _allowed = allowed;
    }

    public IReadOnlyList<TState> AllowedFrom(TState from) => _allowed[from];

    public bool CanMove(TState from, TState to) => _allowed[from].Contains(to);

    /// <summary>Success when the move is legal, otherwise a 409 listing the legal targets.</summary>
    public Result EnsureCanMove(TState from, TState to, string entityName) =>
        CanMove(from, to)
            ? Result.Success()
            : Error.Conflict(
                "illegal_transition",
                $"{entityName} cannot move from {from} to {to}. Allowed: {string.Join(", ", AllowedFrom(from))}.");
}
