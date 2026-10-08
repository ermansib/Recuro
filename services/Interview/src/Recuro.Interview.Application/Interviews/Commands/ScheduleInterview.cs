using FluentValidation;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Domain.Interviews;

namespace Recuro.Interview.Application.Interviews.Commands;

/// <summary>Body of <c>POST /api/v1/interviews</c>.</summary>
public sealed record ScheduleInterviewRequest(
    string? AppId,
    string? RoundType,
    InterviewMode? Mode,
    DateTimeOffset? ScheduledFor,
    int? DurationMinutes,
    IReadOnlyList<PanelMemberRequest>? Panel);

public sealed record PanelMemberRequest(string? Id, string? Name);

/// <summary>
/// RCU-INT-001: HR-TA schedules a round. The round type must be in the grade's template, the form's rows
/// are the JD's competencies (RCU-INT-002), the JD's assessment material is attached, the panel gets a
/// calendar invite and <c>interview.scheduled</c> queues the candidate's confirmation.
/// </summary>
public sealed record ScheduleInterviewCommand(ScheduleInterviewRequest Request) : ICommand<InterviewDto>
{
    public const int DefaultDurationMinutes = 60;
}

internal sealed class ScheduleInterviewCommandValidator : AbstractValidator<ScheduleInterviewCommand>
{
    public ScheduleInterviewCommandValidator()
    {
        RuleFor(c => c.Request).NotNull();
        RuleFor(c => c.Request.AppId).NotEmpty().MaximumLength(InterviewLimits.IdLength).OverridePropertyName("appId");
        RuleFor(c => c.Request.RoundType).NotEmpty().MaximumLength(InterviewLimits.IdLength).OverridePropertyName("roundType");
        RuleFor(c => c.Request.Mode).NotNull().IsInEnum().OverridePropertyName("mode");
        RuleFor(c => c.Request.ScheduledFor).NotNull().OverridePropertyName("scheduledFor");
        RuleFor(c => c.Request.DurationMinutes)
            .InclusiveBetween(InterviewLimits.MinDurationMinutes, InterviewLimits.MaxDurationMinutes)
            .OverridePropertyName("durationMinutes");
        RuleFor(c => c.Request.Panel)
            .NotEmpty()
            .Must(p => p!.Count <= InterviewLimits.MaxPanel).WithMessage($"At most {InterviewLimits.MaxPanel} panel members.")
            .Must(p => p!.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() == p!.Count).WithMessage("Each panel member appears once.")
            .OverridePropertyName("panel");
        RuleForEach(c => c.Request.Panel).ChildRules(member =>
        {
            member.RuleFor(m => m.Id).NotEmpty().MaximumLength(InterviewLimits.ShortText);
            member.RuleFor(m => m.Name).NotEmpty().MaximumLength(InterviewLimits.ShortText);
        }).OverridePropertyName("panel");
    }
}

internal sealed partial class ScheduleInterviewCommandHandler(
    IApplicationTrackRepository tracks,
    IInterviewRepository interviews,
    IJobDescriptions jobDescriptions,
    IInterviewRulesSource rulesSource,
    ICalendarInvites calendar,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<ScheduleInterviewCommandHandler> logger) : ICommandHandler<ScheduleInterviewCommand, InterviewDto>
{
    public async Task<Result<InterviewDto>> Handle(ScheduleInterviewCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var appId = request.AppId!.Trim();
        var now = clock.GetUtcNow();
        if (request.ScheduledFor!.Value <= now)
        {
            return Error.Validation([new FieldError("scheduledFor", "in_past", "Schedule the round in the future.")]);
        }

        var track = await tracks.GetAsync(appId, ct);
        if (track is not { InInterview: true })
        {
            return InterviewErrors.NotInInterviewStage(appId);
        }

        var jd = await jobDescriptions.GetAsync(track.ReqId, ct);
        if (jd is null || jd.Competencies.Count == 0)
        {
            return InterviewErrors.JobDescriptionMissing(track.ReqId);
        }

        var rules = await rulesSource.GetAsync(ct);
        if (!rules.HasTemplate(jd.Grade))
        {
            return InterviewErrors.NoTemplate(jd.Grade);
        }

        var template = rules.FindRound(jd.Grade, request.RoundType!.Trim());
        if (template is null)
        {
            return InterviewErrors.RoundNotAllowed(request.RoundType.Trim(), jd.Grade);
        }

        var existing = await interviews.ListForApplicationAsync(appId, ct);
        var plan = new RoundPlan(
            appId,
            track.ReqId,
            track.CandidateId,
            jd.Grade,
            template.Type,
            template.Label,
            existing.Count(r => r.Status != InterviewStatus.Cancelled) + 1,
            request.Mode!.Value,
            request.ScheduledFor.Value,
            request.DurationMinutes ?? ScheduleInterviewCommand.DefaultDurationMinutes,
            request.Panel!.Select(p => new PanelMember(p.Id!.Trim(), p.Name!.Trim())).ToList(),
            jd.Competencies,
            jd.Assessments,
            rules.Sla);

        var round = InterviewRound.Schedule(plan, user.Name ?? user.UserId ?? string.Empty, now);
        interviews.Add(round);
        await unitOfWork.SaveChangesAsync(ct);

        await Invites.SendAsync(calendar, round, logger, ct);
        return InterviewDto.From(round);
    }
}

/// <summary>Calendar invites are best effort: the round is booked even if the calendar adapter fails.</summary>
internal static partial class Invites
{
    public static CalendarInvite For(InterviewRound round) =>
        new(round.Id, round.Title, round.ScheduledFor, round.EndsAt, round.Mode, round.Assessments.Select(a => a.InterviewerId).ToList());

    public static async Task SendAsync(ICalendarInvites calendar, InterviewRound round, ILogger logger, CancellationToken ct)
    {
        try
        {
            await calendar.SendAsync(For(round), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            InviteFailed(logger, round.Id, ex);
        }
    }

    public static async Task CancelAsync(ICalendarInvites calendar, CalendarInvite invite, ILogger logger, CancellationToken ct)
    {
        try
        {
            await calendar.CancelAsync(invite, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            InviteFailed(logger, invite.InterviewId, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Calendar adapter failed for interview {InterviewId}; the round stands")]
    private static partial void InviteFailed(ILogger logger, Guid interviewId, Exception ex);
}
