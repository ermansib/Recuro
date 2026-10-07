using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Pipeline.Application.Abstractions;

namespace Recuro.Pipeline.Application.Applications.Commands.ScanTatBreaches;

/// <summary>
/// RCU-PPL-005: flags the current tenant's applications that overran their stage TAT, once per stage.
/// Each breach becomes a <c>pipeline.tat.breached</c> event. Returns how many were flagged.
/// </summary>
public sealed record ScanTatBreachesCommand(int BatchSize = 500) : ICommand<int>;

internal sealed class ScanTatBreachesCommandHandler(
    IApplicationRepository applications,
    IUnitOfWork unitOfWork,
    ITatRules tatRules,
    IWorkingDayCalendar calendar,
    TimeProvider clock) : ICommandHandler<ScanTatBreachesCommand, int>
{
    public async Task<Result<int>> Handle(ScanTatBreachesCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var flagged = 0;
        foreach (var (stage, rule) in await tatRules.GetAsync(ct))
        {
            if (rule.WorkingDays <= 0)
            {
                continue;
            }

            // N working days always span at least N calendar days, so this narrows the scan in the database.
            var candidates = await applications.ListTatCandidatesAsync(stage, now.AddDays(-rule.WorkingDays), command.BatchSize, ct);
            foreach (var application in candidates)
            {
                var entered = application.StageEnteredAt.ToUniversalTime();
                var dueDate = await calendar.AddAsync(DateOnly.FromDateTime(entered.UtcDateTime), rule.WorkingDays, ct);
                var dueAt = new DateTimeOffset(dueDate.ToDateTime(TimeOnly.FromDateTime(entered.UtcDateTime)), TimeSpan.Zero);
                if (application.FlagTatBreach(dueAt, now, string.IsNullOrEmpty(rule.Escalation) ? null : rule.Escalation))
                {
                    flagged++;
                }
            }
        }

        if (flagged > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return flagged;
    }
}
