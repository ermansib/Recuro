namespace Recuro.Config.Domain.Rules;

/// <summary>Working week and holidays for one location (BNFR-8: SLA math uses business days per location).</summary>
public sealed record BusinessCalendar(string Location, IReadOnlyList<DayOfWeek> WeekendDays, IReadOnlyList<DateOnly> Holidays)
{
    public bool IsWorkingDay(DateOnly day) => !WeekendDays.Contains(day.DayOfWeek) && !Holidays.Contains(day);

    /// <summary>
    /// <paramref name="from"/> plus <paramref name="days"/> working days: the start day itself never
    /// counts, 0 returns <paramref name="from"/>, and negative days count backwards (T-5 before a joining
    /// date). The only working-day calculation in the backend.
    /// </summary>
    public DateOnly AddWorkingDays(DateOnly from, int days)
    {
        if (WeekendDays.Distinct().Count() >= 7)
        {
            throw new InvalidOperationException($"Calendar '{Location}' has no working days.");
        }

        var step = days < 0 ? -1 : 1;
        var day = from;
        for (var added = 0; added < Math.Abs(days);)
        {
            day = day.AddDays(step);
            if (IsWorkingDay(day))
            {
                added++;
            }
        }

        return day;
    }
}

/// <summary>Business calendars per location; <see cref="DefaultLocation"/> applies when none is given.</summary>
public sealed record CalendarMatrix(string DefaultLocation, IReadOnlyList<BusinessCalendar> Calendars)
{
    /// <summary>The calendar for <paramref name="location"/>, or the default one when it is empty. Null when unknown.</summary>
    public BusinessCalendar? Find(string? location)
    {
        var key = string.IsNullOrWhiteSpace(location) ? DefaultLocation : location;
        return Calendars.FirstOrDefault(c => string.Equals(c.Location, key, StringComparison.OrdinalIgnoreCase));
    }
}
