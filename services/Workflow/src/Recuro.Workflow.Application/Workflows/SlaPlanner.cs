using Recuro.Workflow.Application.Abstractions;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.Application.Workflows;

/// <summary>
/// RCU-WFL-003/006: turns a leg's SLA and escalation ladder (working days) into instants on the tenant's
/// business calendar, using the config version the workflow is pinned to. The deadline keeps the time of
/// day the task was created; escalation steps count from the deadline; the 50% reminder counts half the SLA.
/// </summary>
public sealed class SlaPlanner(IBusinessCalendar calendar)
{
    public async Task<TaskSchedule> ScheduleAsync(LegDefinition? leg, string configVersionId, DateTimeOffset now, CancellationToken ct)
    {
        if (leg?.SlaWorkingDays is not { } sla)
        {
            return TaskSchedule.None;
        }

        var utc = now.ToUniversalTime();
        var today = DateOnly.FromDateTime(utc.UtcDateTime);
        var dueDate = await calendar.AddWorkingDaysAsync(today, sla, configVersionId, ct);
        var escalations = new List<DateTimeOffset>();
        foreach (var step in leg.Escalation)
        {
            var at = await calendar.AddWorkingDaysAsync(dueDate, step.AfterWorkingDays, configVersionId, ct);
            escalations.Add(At(at, utc));
        }

        var due = At(dueDate, utc);

        // The 50% reminder (RCU-WFL-003): half the working days, rounded down; a one-day SLA reminds halfway in clock time.
        var halfway = sla / 2 == 0
            ? utc + ((due - utc) / 2)
            : At(await calendar.AddWorkingDaysAsync(today, sla / 2, configVersionId, ct), utc);
        return new TaskSchedule(due, escalations, halfway);
    }

    private static DateTimeOffset At(DateOnly date, DateTimeOffset timeOfDay) =>
        new(date.ToDateTime(TimeOnly.FromTimeSpan(timeOfDay.TimeOfDay)), TimeSpan.Zero);
}
