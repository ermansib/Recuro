using System.Text.RegularExpressions;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Offer.Application.Abstractions;

namespace Recuro.Offer.Application.Offers;

/// <summary>
/// RCU-AUT-004 on offers. Maps come from Identity (<c>GET /api/v1/identity/masking/{role}/offer</c>);
/// a role without a map fails closed, so the CTC and the candidate's name are hidden. Amounts are either
/// shown or not: a partial salary would still leak it.
/// </summary>
public static partial class OfferMasking
{
    public const string Resource = "offer";

    public const string CandidateName = "candidateName";
    public const string Components = "components";
    public const string Band = "band";

    public static readonly IReadOnlyList<string> SensitiveFields = [CandidateName, Components, Band];

    private const string Hidden = "hide";

    /// <summary>The FRD §3.2 map, used only while Identity can't be reached and nothing is cached.</summary>
    public static IReadOnlyDictionary<string, string>? LocalFallback(string role) => role switch
    {
        "hrta" or "hrhead" => new Dictionary<string, string>(StringComparer.Ordinal),
        "mdceo" => new Dictionary<string, string>(StringComparer.Ordinal) { [Components] = Hidden, [Band] = Hidden },
        _ => null,
    };

    /// <summary>
    /// Per field, the least restrictive answer across the caller's roles: visible if any role's map leaves
    /// it unmasked. No maps at all hides every sensitive field.
    /// </summary>
    public static IReadOnlySet<string> HiddenFields(IEnumerable<IReadOnlyDictionary<string, string>?> roleMaps)
    {
        ArgumentNullException.ThrowIfNull(roleMaps);
        var maps = roleMaps.OfType<IReadOnlyDictionary<string, string>>().ToList();
        if (maps.Count == 0)
        {
            return SensitiveFields.ToHashSet(StringComparer.Ordinal);
        }

        return SensitiveFields.Where(f => maps.All(m => m.ContainsKey(f))).ToHashSet(StringComparer.Ordinal);
    }

    public static OfferDto Apply(OfferDto offer, IReadOnlySet<string> hidden)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(hidden);
        if (hidden.Count == 0)
        {
            return offer;
        }

        var hideAmounts = hidden.Contains(Components) || hidden.Contains(Band);
        return offer with
        {
            CandidateName = hidden.Contains(CandidateName) ? string.Empty : offer.CandidateName,
            Components = hidden.Contains(Components) ? null : offer.Components,
            Band = hidden.Contains(Band) ? null : offer.Band,
            Route = hideAmounts && offer.Route is { } r ? r with { Total = null, Deviation = null } : offer.Route,
            Trail = hideAmounts ? offer.Trail.Select(t => t with { Title = Amounts().Replace(t.Title, "₹•••"), Detail = Amounts().Replace(t.Detail, "₹•••") }).ToList() : offer.Trail,
        };
    }

    [GeneratedRegex(@"₹\s?[0-9][0-9.,]*\s?L?", RegexOptions.CultureInvariant)]
    private static partial Regex Amounts();
}

/// <summary>Masks offers for the current caller, from the Identity maps of each of their roles.</summary>
public interface ICallerMask
{
    Task<Func<OfferDto, OfferDto>> ForCallerAsync(CancellationToken ct);
}

internal sealed class CallerMask(ICurrentUser caller, IMaskingMaps maps) : ICallerMask
{
    public async Task<Func<OfferDto, OfferDto>> ForCallerAsync(CancellationToken ct)
    {
        var roleMaps = new List<IReadOnlyDictionary<string, string>?>();
        foreach (var role in caller.Roles.Distinct(StringComparer.Ordinal))
        {
            roleMaps.Add(await maps.GetAsync(role, ct));
        }

        var hidden = OfferMasking.HiddenFields(roleMaps);
        return offer => OfferMasking.Apply(offer, hidden);
    }
}
