namespace Recuro.Gateway.Bff;

/// <summary>Bound from <c>Bff:Dashboard</c>. Which services feed the dashboard is configuration, not code.</summary>
internal sealed class DashboardOptions
{
    public const string SectionName = "Bff:Dashboard";

    /// <summary>Per-source budget. A slower source is shown as unavailable instead of holding the page.</summary>
    public TimeSpan SourceTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Culture for the date and period labels.</summary>
    public string Culture { get; set; } = "en-GB";

    /// <summary>Sources in tile order. Keyed by name, e.g. <c>requisition</c>.</summary>
    public Dictionary<string, DashboardSourceOptions> Sources { get; init; } = [];
}

internal sealed class DashboardSourceOptions
{
    /// <summary>Position on the page; lower first.</summary>
    public int Order { get; set; }

    /// <summary>Absolute URL of the service's fragment endpoint.</summary>
    public Uri? Url { get; set; }

    /// <summary>The tiles this source owns, shown with value "—" when it is unavailable.</summary>
    public List<PlaceholderTile> Tiles { get; init; } = [];
}

internal sealed class PlaceholderTile
{
    public string Label { get; set; } = string.Empty;

    public string Tone { get; set; } = string.Empty;
}
