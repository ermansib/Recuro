using System.Globalization;

namespace Recuro.Reporting.Domain.Periods;

/// <summary>How often a report period rolls up: monthly packs go to HR Head, quarterly to MD/CEO (FRD §12).</summary>
public enum Cadence
{
    Monthly,
    Quarterly,
}

/// <summary>
/// A calendar month (<c>2026-08</c>) or quarter (<c>2026-Q3</c>). Boundaries are local dates; callers place
/// them in the tenant's time zone with <see cref="InZone"/>.
/// </summary>
public sealed record ReportPeriod
{
    private ReportPeriod(Cadence cadence, int year, int index)
    {
        Cadence = cadence;
        Year = year;
        Index = index;
    }

    public Cadence Cadence { get; }

    public int Year { get; }

    /// <summary>Month 1–12, or quarter 1–4.</summary>
    public int Index { get; }

    public string Key => Cadence == Cadence.Monthly
        ? string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Index:D2}")
        : string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-Q{Index}");

    /// <summary>English label, e.g. <c>Aug 2026</c> or <c>Q3 2026</c>, as the dashboard shows it.</summary>
    public string Label => Cadence == Cadence.Monthly
        ? FirstDay.ToString("MMM yyyy", CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"Q{Index} {Year}");

    public DateOnly FirstDay => Cadence == Cadence.Monthly
        ? new DateOnly(Year, Index, 1)
        : new DateOnly(Year, ((Index - 1) * 3) + 1, 1);

    /// <summary>The first day after the period.</summary>
    public DateOnly EndExclusive => Cadence == Cadence.Monthly ? FirstDay.AddMonths(1) : FirstDay.AddMonths(3);

    public static ReportPeriod Month(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        return new ReportPeriod(Cadence.Monthly, year, month);
    }

    public static ReportPeriod Quarter(int year, int quarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, 4);
        return new ReportPeriod(Cadence.Quarterly, year, quarter);
    }

    public static ReportPeriod Containing(Cadence cadence, DateOnly day) => cadence == Cadence.Monthly
        ? Month(day.Year, day.Month)
        : Quarter(day.Year, ((day.Month - 1) / 3) + 1);

    /// <summary>Parses <c>yyyy-MM</c> or <c>yyyy-Qn</c>.</summary>
    public static bool TryParse(string? value, out ReportPeriod period)
    {
        period = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 7 or > 7)
        {
            return false;
        }

        if (!int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year < 2000 || year > 9999 || value[4] != '-')
        {
            return false;
        }

        if (value[5] is 'Q' or 'q' && value[6] is >= '1' and <= '4')
        {
            period = Quarter(year, value[6] - '0');
            return true;
        }

        if (int.TryParse(value.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month) && month is >= 1 and <= 12)
        {
            period = Month(year, month);
            return true;
        }

        return false;
    }

    public ReportPeriod Previous() => Cadence == Cadence.Monthly
        ? Containing(Cadence, FirstDay.AddMonths(-1))
        : Containing(Cadence, FirstDay.AddMonths(-3));

    /// <summary>The periods ending with this one, oldest first (for trend sparklines).</summary>
    public IReadOnlyList<ReportPeriod> Trailing(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var periods = new List<ReportPeriod> { this };
        while (periods.Count < count)
        {
            periods.Add(periods[^1].Previous());
        }

        periods.Reverse();
        return periods;
    }

    /// <summary>The period's start and end instants in <paramref name="zone"/>.</summary>
    public ReportWindow InZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return new ReportWindow(StartOf(FirstDay, zone), StartOf(EndExclusive, zone));
    }

    public override string ToString() => Key;

    private static DateTimeOffset StartOf(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}

/// <summary>A half-open time range [<see cref="From"/>, <see cref="To"/>).</summary>
public sealed record ReportWindow(DateTimeOffset From, DateTimeOffset To)
{
    public bool Contains(DateTimeOffset? at) => at is { } value && value >= From && value < To;
}
