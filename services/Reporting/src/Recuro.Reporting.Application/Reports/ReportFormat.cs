using System.Globalization;
using Recuro.Reporting.Domain.Kpis;

namespace Recuro.Reporting.Application.Reports;

/// <summary>
/// Display strings for the register, in the shape the frontend's dashboard mock uses (<c>"87%"</c>,
/// <c>"21d"</c>, <c>"₹42K"</c>, status <c>"✓"</c> with tone <c>green</c>). Numbers are also returned raw,
/// so a client can format them for its own locale.
/// </summary>
public static class ReportFormat
{
    public const string None = "—";

    public static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    public static string Value(MetricDefinition definition, KpiMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(measurement);
        if (measurement.Actual is not { } actual)
        {
            return None;
        }

        return definition.Unit switch
        {
            MetricUnit.Days => Days(actual),
            MetricUnit.Percent => Percent(actual),
            MetricUnit.Money => Money(actual, measurement.Currency),
            MetricUnit.Mix => measurement.Mix?.FirstOrDefault(s => s.Hires > 0) is { } top
                ? string.Create(CultureInfo.InvariantCulture, $"{top.Source} {Percent(top.Percent)}")
                : None,
            _ => actual.ToString(CultureInfo.InvariantCulture),
        };
    }

    public static string Days(decimal days) =>
        string.Create(CultureInfo.InvariantCulture, $"{(days < 10 ? days.ToString("0.#", CultureInfo.InvariantCulture) : Math.Round(days, 0).ToString("0", CultureInfo.InvariantCulture))} d");

    public static string Percent(decimal percent) =>
        string.Create(CultureInfo.InvariantCulture, $"{percent.ToString("0.#", CultureInfo.InvariantCulture)}%");

    /// <summary>Compact money, e.g. <c>₹42K</c> or <c>$1.2M</c>.</summary>
    public static string Money(decimal amount, string? currency)
    {
        var symbol = currency?.ToUpperInvariant() switch
        {
            "INR" => "₹",
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            null or "" => string.Empty,
            var code => code + " ",
        };
        var abs = Math.Abs(amount);
        var compact = abs >= 1_000_000m
            ? (amount / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture) + "M"
            : abs >= 1_000m
                ? (amount / 1_000m).ToString("0.#", CultureInfo.InvariantCulture) + "K"
                : amount.ToString("0", CultureInfo.InvariantCulture);
        return symbol + compact;
    }

    public static (string Status, string Tone) Status(KpiStatus status) => status switch
    {
        KpiStatus.OnTrack => ("✓", "green"),
        KpiStatus.Near => ("⚠ near", "amber"),
        KpiStatus.OffTrack => ("✗ off", "red"),
        KpiStatus.Review => ("review", "slate"),
        _ => ("no data", "slate"),
    };

    public static string Unit(MetricUnit unit) => unit switch
    {
        MetricUnit.Days => "days",
        MetricUnit.Percent => "percent",
        MetricUnit.Money => "money",
        _ => "mix",
    };

    public static string Stage(Domain.Projections.FunnelStage stage) => stage switch
    {
        Domain.Projections.FunnelStage.Sourced => "Sourced",
        Domain.Projections.FunnelStage.Screened => "Screened",
        Domain.Projections.FunnelStage.Interviewed => "Interviewed",
        Domain.Projections.FunnelStage.Bgv => "BGV",
        Domain.Projections.FunnelStage.Offered => "Offered",
        _ => "Joined",
    };
}
