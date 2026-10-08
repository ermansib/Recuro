namespace Recuro.Bgv.Domain.Cases;

/// <summary>
/// Working days elapsed on a case, for the TAT counter shown on the tracker (tatDay). Weekends only;
/// due dates themselves come from the tenant's business calendar in the Config service.
/// </summary>
public static class WorkingDays
{
    public static bool IsWorkingDay(DateOnly day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>Working days after <paramref name="from"/> up to and including <paramref name="to"/>.</summary>
    public static int Between(DateOnly from, DateOnly to)
    {
        var count = 0;
        for (var day = from.AddDays(1); day <= to; day = day.AddDays(1))
        {
            if (IsWorkingDay(day))
            {
                count++;
            }
        }

        return count;
    }
}
