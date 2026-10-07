namespace Recuro.Candidate.Application.Candidates.Masking;

/// <summary>How a field is shown to a role (RCU-AUT-004), from least to most restrictive.</summary>
public enum MaskStrategy
{
    /// <summary>Shown as is.</summary>
    None,

    /// <summary>Only the last 4 characters are kept, e.g. <c>••••0001</c>.</summary>
    Partial,

    /// <summary>A stable one-way hash, so equal values still group together.</summary>
    Hash,

    /// <summary>Not returned (empty or null).</summary>
    Hide,
}

/// <summary>
/// RCU-CND-004: field masking per caller role. The maps come from the Identity service
/// (<c>GET /api/v1/identity/masking/{role}/candidate</c>, RCU-AUT-004); this class turns them into one mask
/// for the caller and applies it. Missing maps fail closed: every sensitive field is hidden.
/// </summary>
public static class CandidateMasking
{
    public const string Resource = "candidate";

    public const string Name = "name";
    public const string Email = "email";
    public const string Phone = "phone";
    public const string Summary = "summary";
    public const string CurrentCtc = "currentCtc";
    public const string ExpectedCtc = "expectedCtc";

    private const int PartialKeep = 4;
    private const string PartialPrefix = "••••";

    /// <summary>The <c>Candidate</c> fields a map can mask (frontend DTO names).</summary>
    public static readonly IReadOnlyList<string> SensitiveFields = [Name, Email, Phone, Summary, CurrentCtc, ExpectedCtc];

    public static readonly IReadOnlyDictionary<string, MaskStrategy> Unmasked =
        SensitiveFields.ToDictionary(f => f, _ => MaskStrategy.None, StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, MaskStrategy> FailClosed =
        SensitiveFields.ToDictionary(f => f, _ => MaskStrategy.Hide, StringComparer.Ordinal);

    /// <summary>
    /// The FRD §3.2 map, used only while the Identity service can't be reached and nothing is cached.
    /// It matches Identity's default map; any other role fails closed.
    /// </summary>
    public static IReadOnlyDictionary<string, MaskStrategy>? LocalFallback(string role) => role switch
    {
        "hrta" or "hrhead" => new Dictionary<string, MaskStrategy>(StringComparer.Ordinal),
        "mdceo" => new Dictionary<string, MaskStrategy>(StringComparer.Ordinal)
        {
            [Email] = MaskStrategy.Partial,
            [Phone] = MaskStrategy.Partial,
            [CurrentCtc] = MaskStrategy.Hide,
            [ExpectedCtc] = MaskStrategy.Hide,
        },
        _ => null,
    };

    /// <summary>Reads Identity's wire map (<c>hide</c>, <c>partial</c>, <c>hash</c>). An unknown strategy hides the field.</summary>
    public static IReadOnlyDictionary<string, MaskStrategy> Parse(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields.ToDictionary(
            f => f.Key,
            f => f.Value switch
            {
                "partial" => MaskStrategy.Partial,
                "hash" => MaskStrategy.Hash,
                _ => MaskStrategy.Hide,
            },
            StringComparer.Ordinal);
    }

    /// <summary>
    /// One mask for a caller with several roles: per field, the least restrictive strategy any role allows.
    /// A role without a map (null) contributes nothing; no maps at all fails closed.
    /// </summary>
    public static IReadOnlyDictionary<string, MaskStrategy> Combine(IEnumerable<IReadOnlyDictionary<string, MaskStrategy>?> roleMaps)
    {
        ArgumentNullException.ThrowIfNull(roleMaps);
        var maps = roleMaps.OfType<IReadOnlyDictionary<string, MaskStrategy>>().ToList();
        if (maps.Count == 0)
        {
            return FailClosed;
        }

        // A field a map doesn't list passes through unchanged for that role.
        return SensitiveFields.ToDictionary(f => f, f => maps.Min(m => m.GetValueOrDefault(f, MaskStrategy.None)), StringComparer.Ordinal);
    }

    public static CandidateDto Apply(CandidateDto candidate, IReadOnlyDictionary<string, MaskStrategy> mask, Func<string, string, string> hash)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(hash);

        string Text(string field, string value) => mask.GetValueOrDefault(field, MaskStrategy.Hide) switch
        {
            MaskStrategy.None => value,
            MaskStrategy.Partial => Partial(value),
            MaskStrategy.Hash when value.Length > 0 => hash(field, value),
            _ => string.Empty,
        };

        // Amounts are either shown or not: a partial or hashed salary would still leak it.
        decimal? Amount(string field, decimal? value) => mask.GetValueOrDefault(field, MaskStrategy.Hide) == MaskStrategy.None ? value : null;

        return candidate with
        {
            Name = Text(Name, candidate.Name),
            Email = Text(Email, candidate.Email),
            Phone = Text(Phone, candidate.Phone),
            Summary = Text(Summary, candidate.Summary),
            CurrentCtc = Amount(CurrentCtc, candidate.CurrentCtc),
            ExpectedCtc = Amount(ExpectedCtc, candidate.ExpectedCtc),
        };
    }

    /// <summary>Identity's <c>partial</c>: only the last 4 characters stay, e.g. <c>+91 98200 40001</c> → <c>••••0001</c>.</summary>
    public static string Partial(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length == 0 ? string.Empty : PartialPrefix + value[^Math.Min(PartialKeep, value.Length)..];
    }
}
