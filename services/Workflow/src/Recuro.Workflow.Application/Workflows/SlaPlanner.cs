using Recuro.Workflow.Application.Abstractions;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.Application.Workflows;

/// <summary>
/// RCU-WFL-003/006: turns a leg's SLA and escalation ladder (working days) into instants on the tenant's
/// business calendar, using the config version the workflow is pinned to. The deadline keeps the time of
/// day the task was created; escalation steps count from the deadline.
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

        return new TaskSchedule(At(dueDate, utc), escalations);
    }

    private static DateTimeOffset At(DateOnly date, DateTimeOffset timeOfDay) =>
        new(date.ToDateTime(TimeOnly.FromTimeSpan(timeOfDay.TimeOfDay)), TimeSpan.Zero);
}
