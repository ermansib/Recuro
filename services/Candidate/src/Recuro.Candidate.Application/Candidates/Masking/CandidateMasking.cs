namespace Recuro.Candidate.Application.Candidates.Masking;

/// <summary>How a field is shown to a role (RCU-AUT-004 vocabulary).</summary>
public enum MaskStrategy
{
    /// <summary>Shown as is.</summary>
    None,

    /// <summary>Partly hidden, e.g. <c>r.***@email.example</c>.</summary>
    Partial,

    /// <summary>Not returned at all (null or empty).</summary>
    Hide,
}

/// <summary>
/// RCU-CND-004: field masking per caller role, kept as data. It mirrors FRD §3.2 and the frontend's
/// <c>candidate.viewSensitive</c> capability (HR-TA and HR Head see everything; MD/CEO sees CTC masked).
/// When the Identity service publishes its masking map (RCU-AUT-004) this table is where it plugs in.
/// Unknown roles fail closed: everything sensitive is hidden.
/// </summary>
public static class CandidateMasking
{
    public const string Email = "email";
    public const string Phone = "phone";
    public const string CurrentCtc = "currentCtc";
    public const string ExpectedCtc = "expectedCtc";

    private static readonly string[] Fields = [Email, Phone, CurrentCtc, ExpectedCtc];

    private static readonly IReadOnlyDictionary<string, MaskStrategy> NothingMasked =
        Fields.ToDictionary(f => f, _ => MaskStrategy.None);

    private static readonly IReadOnlyDictionary<string, MaskStrategy> EverythingHidden =
        Fields.ToDictionary(f => f, _ => MaskStrategy.Hide);

    private static readonly Dictionary<string, IReadOnlyDictionary<string, MaskStrategy>> ByRole = new(StringComparer.Ordinal)
    {
        ["hrta"] = NothingMasked,
        ["hrhead"] = NothingMasked,
        ["service"] = NothingMasked,
        ["mdceo"] = new Dictionary<string, MaskStrategy>
        {
            [Email] = MaskStrategy.Partial,
            [Phone] = MaskStrategy.Partial,
            [CurrentCtc] = MaskStrategy.Hide,
            [ExpectedCtc] = MaskStrategy.Hide,
        },
    };

    /// <summary>The least restrictive strategy any of the caller's roles allows, per field.</summary>
    public static IReadOnlyDictionary<string, MaskStrategy> For(IEnumerable<string> roles)
    {
        var maps = roles.Select(r => ByRole.GetValueOrDefault(r)).OfType<IReadOnlyDictionary<string, MaskStrategy>>().ToList();
        if (maps.Count == 0)
        {
            return EverythingHidden;
        }

        return Fields.ToDictionary(f => f, f => maps.Min(m => m.GetValueOrDefault(f, MaskStrategy.Hide)));
    }

    public static CandidateDto Apply(CandidateDto candidate, IReadOnlyDictionary<string, MaskStrategy> masks)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(masks);
        return candidate with
        {
            Email = MaskEmail(candidate.Email, masks[Email]),
            Phone = MaskPhone(candidate.Phone, masks[Phone]),
            CurrentCtc = masks[CurrentCtc] == MaskStrategy.None ? candidate.CurrentCtc : null,
            ExpectedCtc = masks[ExpectedCtc] == MaskStrategy.None ? candidate.ExpectedCtc : null,
        };
    }

    /// <summary><c>rahul.mehta@email.example</c> → <c>r***@email.example</c>, like the frontend's <c>maskEmail</c>.</summary>
    public static string MaskEmail(string email, MaskStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(email);
        return strategy switch
        {
            MaskStrategy.None => email,
            MaskStrategy.Partial when email.IndexOf('@', StringComparison.Ordinal) is > 0 and var at => $"{email[0]}***{email[at..]}",
            _ => string.Empty,
        };
    }

    /// <summary><c>+91 98200 40001</c> → <c>••••••0001</c>: only the last four digits stay.</summary>
    public static string MaskPhone(string phone, MaskStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(phone);
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        return strategy switch
        {
            MaskStrategy.None => phone,
            MaskStrategy.Partial when digits.Length >= 4 => $"••••••{digits[^4..]}",
            _ => string.Empty,
        };
    }
}
