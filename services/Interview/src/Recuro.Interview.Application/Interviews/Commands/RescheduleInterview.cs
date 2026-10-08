using FluentValidation;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Domain.Interviews;

namespace Recuro.Interview.Application.Interviews.Commands;

public sealed record RescheduleRequest(DateTimeOffset? ScheduledFor, string? Reason);

/// <summary>
/// RCU-INT-007: move a round before any feedback is in. The old invite is cancelled and a new one sent,
/// and <c>interview.scheduled</c> is published again with <c>rescheduledFrom</c>, so both are on record.
/// </summary>
public sealed record RescheduleInterviewCommand(Guid InterviewId, RescheduleRequest Request) : ICommand<InterviewDto>;

internal sealed class RescheduleInterviewCommandValidator : AbstractValidator<RescheduleInterviewCommand>
{
    public RescheduleInterviewCommandValidator()
    {
        RuleFor(c => c.Request).NotNull();
        RuleFor(c => c.Request.ScheduledFor).NotNull().OverridePropertyName("scheduledFor");
        RuleFor(c => c.Request.Reason).MaximumLength(InterviewLimits.LongText).OverridePropertyName("reason");
    }
}

internal sealed class RescheduleInterviewCommandHandler(
    IInterviewRepository interviews,
    IInterviewRulesSource rulesSource,
    ICalendarInvites calendar,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<RescheduleInterviewCommandHandler> logger) : ICommandHandler<RescheduleInterviewCommand, InterviewDto>
{
    public async Task<Result<InterviewDto>> Handle(RescheduleInterviewCommand command, CancellationToken ct)
    {
        if (command.Request.ScheduledFor!.Value <= clock.GetUtcNow())
        {
            return Error.Validation([new FieldError("scheduledFor", "in_past", "Schedule the round in the future.")]);
        }

        var round = await interviews.GetAsync(command.InterviewId, ct);
        if (round is null)
        {
            return InterviewErrors.NotFound(command.InterviewId);
        }

        var previous = Invites.For(round);
        var rules = await rulesSource.GetAsync(ct);
        var moved = round.Reschedule(command.Request.ScheduledFor.Value, rules.Sla, command.Request.Reason ?? string.Empty);
        if (moved.IsFailure)
        {
            return moved.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        await Invites.CancelAsync(calendar, previous, logger, ct);
        await Invites.SendAsync(calendar, round, logger, ct);
        return InterviewDto.From(round);
    }
}
