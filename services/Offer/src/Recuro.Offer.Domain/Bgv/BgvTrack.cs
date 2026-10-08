using Recuro.BuildingBlocks.Domain;

namespace Recuro.Offer.Domain.Bgv;

/// <summary>Where an application's background verification stands, as far as offer release cares.</summary>
public enum BgvGate
{
    InProgress,
    Cleared,
    Adverse,
}

/// <summary>
/// This service's copy of the BGV release gate (RCU-OFR-007), kept from <c>bgv.*</c> events so a send
/// needs no call to the Bgv service. No row means BGV was never initiated: release is blocked.
/// </summary>
public sealed class BgvTrack : ITenantOwned
{
    private BgvTrack()
    {
    }

    public Guid TenantId { get; private set; }

    public string AppId { get; private set; } = string.Empty;

    public BgvGate Gate { get; private set; }

    /// <summary>Checks still holding release, e.g. "Education" after an adverse finding.</summary>
    public IReadOnlyList<string> Blockers { get; private set; } = [];

    /// <summary>Time of the event that set the gate. Older events are ignored.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public static BgvTrack Start(string appId, DateTimeOffset at) => new() { AppId = appId, Gate = BgvGate.InProgress, UpdatedAt = at };

    public void Apply(BgvGate gate, IReadOnlyList<string> blockers, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(blockers);
        if (at < UpdatedAt)
        {
            return;
        }

        Gate = gate;
        Blockers = blockers.ToList();
        UpdatedAt = at;
    }

    /// <summary>What still blocks release; empty when the offer may be sent.</summary>
    public static IReadOnlyList<string> BlockersFor(BgvTrack? track) => track switch
    {
        null => ["Background verification has not been initiated"],
        { Gate: BgvGate.Cleared } => [],
        { Gate: BgvGate.Adverse } t => t.Blockers.Count > 0 ? t.Blockers.Select(b => $"Adverse finding: {b}").ToList() : ["Adverse BGV finding under review"],
        { } t => t.Blockers.Count > 0 ? t.Blockers : ["BGV checks not yet cleared"],
    };
}
