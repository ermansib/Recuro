namespace Recuro.Config.Domain.RuleSets;

/// <summary>The rules matrices Config owns (RCU-CFG-001, FRD §5). Each type is versioned on its own.</summary>
public enum MatrixType
{
    /// <summary>§5.1 Delegation of Authority: MRF approval route per grade.</summary>
    Doa,

    /// <summary>§5.2 TAT standards per stage.</summary>
    Tat,

    /// <summary>§5.3 Offer approval matrix.</summary>
    Offer,

    /// <summary>§5.4 Escalation matrix.</summary>
    Escalation,

    /// <summary>§5.5 BGV check applicability.</summary>
    Bgv,

    /// <summary>Business calendars per location, for every working-day calculation (BNFR-8, BQ-02).</summary>
    Calendar,

    /// <summary>Interview round templates, feedback SLA and selection ratification (RCU-ASM-001/004/006).</summary>
    Interview,
}

public static class MatrixTypes
{
    /// <summary>The wire name, e.g. <c>doa</c>, as used in URLs and events.</summary>
    public static string ToKey(this MatrixType type) => type.ToString().ToLowerInvariant();

    public static bool TryParse(string? key, out MatrixType type)
    {
        type = default;
        return !string.IsNullOrWhiteSpace(key)
            && !int.TryParse(key, out _)
            && Enum.TryParse(key, ignoreCase: true, out type)
            && Enum.IsDefined(type);
    }

    public static IEnumerable<string> Keys => Enum.GetValues<MatrixType>().Select(t => t.ToKey());
}
