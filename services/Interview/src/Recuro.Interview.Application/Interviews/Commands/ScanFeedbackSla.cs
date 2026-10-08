using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Interview.Application.Abstractions;

namespace Recuro.Interview.Application.Interviews.Commands;

/// <summary>
/// RCU-INT-004 for the current tenant: 24h after a round, remind interviewers who haven't submitted;
/// at 48h raise the overdue escalation. Each fires once per schedule. Returns how many events were raised.
/// </summary>
public sealed record ScanFeedbackSlaCommand : ICommand<int>
{
    public const int BatchSize = 200;
}

internal sealed class ScanFeedbackSlaCommandHandler(IInterviewRepository interviews, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<ScanFeedbackSlaCommand, int>
{
    public async Task<Result<int>> Handle(ScanFeedbackSlaCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var raised = 0;
        foreach (var round in await interviews.ListSlaDueAsync(now, ScanFeedbackSlaCommand.BatchSize, ct))
        {
            // Overdue first, so a round found past both thresholds only escalates.
            raised += round.RaiseOverdueIfDue(now) ? 1 : 0;
            raised += round.RaiseReminderIfDue(now) ? 1 : 0;
        }

        if (raised > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return raised;
    }
}
