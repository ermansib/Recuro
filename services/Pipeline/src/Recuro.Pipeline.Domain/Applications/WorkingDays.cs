namespace Recuro.Pipeline.Domain.Applications;

/// <summary>
/// Working-day arithmetic for TATs and the regret deadline (BNFR-8). Weekends only for now; per-location
/// holiday calendars come from the Config service (BQ-02) behind the same methods.
/// </summary>
public static class WorkingDays
{
    public static bool IsWorkingDay(DateOnly day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>The date <paramref name="days"/> working days after <paramref name="from"/>.</summary>
    public static DateOnly Add(DateOnly from, int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);
        var day = from;
        for (var added = 0; added < days;)
        {
            day = day.AddDays(1);
            if (IsWorkingDay(day))
            {
                added++;
            }
        }

        return day;
    }

    /// <summary>The instant <paramref name="days"/> working days after <paramref name="from"/>, same time of day (UTC).</summary>
    public static DateTimeOffset Add(DateTimeOffset from, int days)
    {
        var utc = from.ToUniversalTime();
        var date = Add(DateOnly.FromDateTime(utc.UtcDateTime), days);
        return new DateTimeOffset(date.ToDateTime(TimeOnly.FromDateTime(utc.UtcDateTime)), TimeSpan.Zero);
    }
}
