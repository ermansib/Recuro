using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Domain.Interviews;

namespace Recuro.Interview.Application.Interviews.Commands;

/// <summary>RCU-INT-002: autosave the caller's draft. Send If-Match with the ETag; a stale version is 409.</summary>
public sealed record SaveAssessmentDraftCommand(Guid AssessmentId, uint? ExpectedVersion, AssessmentRequest Request) : ICommand<VersionedRound>;

/// <summary>RCU-INT-003: submit and lock. Publishes <c>interview.feedback.submitted</c> with the average.</summary>
public sealed record SubmitAssessmentCommand(Guid AssessmentId, uint? ExpectedVersion, AssessmentRequest Request) : ICommand<VersionedRound>;

/// <summary>RCU-INT-003: replace submitted feedback with a new revision and a reason; the original is kept.</summary>
public sealed record SupersedeAssessmentCommand(Guid AssessmentId, AssessmentRequest Request) : ICommand<VersionedRound>;

internal sealed class AssessmentRequestValidator : AbstractValidator<AssessmentRequest>
{
    public AssessmentRequestValidator()
    {
        RuleFor(r => r.Ratings).Must(r => r is null || r.Count <= InterviewLimits.MaxCompetencies);
        RuleForEach(r => r.Ratings).ChildRules(row =>
        {
            row.RuleFor(x => x.CompetencyId).NotEmpty().MaximumLength(InterviewLimits.IdLength);
            row.RuleFor(x => x.Comment).MaximumLength(InterviewLimits.LongText);
        });
        RuleFor(r => r.Recommendation).IsInEnum();
        RuleFor(r => r.Justification).MaximumLength(InterviewLimits.LongText);
        RuleFor(r => r.Flags).Must(f => f is null || f.Count <= InterviewLimits.MaxFlags).WithMessage($"At most {InterviewLimits.MaxFlags} flags.");
        RuleForEach(r => r.Flags).MaximumLength(InterviewLimits.ShortText);
        RuleFor(r => r.Reason).MaximumLength(InterviewLimits.LongText);
    }
}

internal sealed class SaveAssessmentDraftCommandValidator : AbstractValidator<SaveAssessmentDraftCommand>
{
    public SaveAssessmentDraftCommandValidator() => RuleFor(c => c.Request).NotNull().SetValidator(new AssessmentRequestValidator());
}

internal sealed class SubmitAssessmentCommandValidator : AbstractValidator<SubmitAssessmentCommand>
{
    public SubmitAssessmentCommandValidator() => RuleFor(c => c.Request).NotNull().SetValidator(new AssessmentRequestValidator());
}

internal sealed class SupersedeAssessmentCommandValidator : AbstractValidator<SupersedeAssessmentCommand>
{
    public SupersedeAssessmentCommandValidator() => RuleFor(c => c.Request).NotNull().SetValidator(new AssessmentRequestValidator());
}

/// <summary>
/// Loads the round holding an assessment and checks the caller may write it: the PDP's
/// <c>assessment.submit</c> allows the assigned interviewer, or HR-TA acting for them (RCU-INT-002).
/// </summary>
internal sealed class AssessmentWriter(
    IInterviewRepository interviews,
    IAccessDecisions access,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock)
{
    public const string Action = "assessment.submit";

    public async Task<Result<VersionedRound>> WriteAsync(
        Guid assessmentId,
        uint? expectedVersion,
        Func<InterviewRound, Assessment, DateTimeOffset, string, Result> change,
        CancellationToken ct)
    {
        var round = await interviews.GetByAssessmentAsync(assessmentId, ct);
        if (round is null)
        {
            return InterviewErrors.AssessmentNotFound(assessmentId);
        }

        var assessment = round.Find(assessmentId).Value;
        if (!await access.AllowedAsync(Action, "assessment", assessmentId.ToString(), [assessment.InterviewerId], ct))
        {
            return Error.Forbidden("not_assigned_interviewer", "Feedback is owned by the assigned interviewer.");
        }

        if (expectedVersion is { } expected && expected != assessment.Version)
        {
            return InterviewErrors.StaleVersion(assessmentId);
        }

        var changed = change(round, assessment, clock.GetUtcNow(), user.Name ?? user.UserId ?? string.Empty);
        if (changed.IsFailure)
        {
            return changed.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        var rounds = await interviews.ListForApplicationAsync(round.AppId, ct);
        return new VersionedRound(InterviewRoundDto.From(round, assessment, rounds), assessment.Version);
    }
}

internal sealed class SaveAssessmentDraftCommandHandler(AssessmentWriter writer) : ICommandHandler<SaveAssessmentDraftCommand, VersionedRound>
{
    public Task<Result<VersionedRound>> Handle(SaveAssessmentDraftCommand command, CancellationToken ct) =>
        writer.WriteAsync(command.AssessmentId, command.ExpectedVersion, (round, assessment, _, _) => round.SaveDraft(assessment.Id, command.Request.ToInput()), ct);
}

internal sealed class SubmitAssessmentCommandHandler(AssessmentWriter writer) : ICommandHandler<SubmitAssessmentCommand, VersionedRound>
{
    public Task<Result<VersionedRound>> Handle(SubmitAssessmentCommand command, CancellationToken ct) =>
        writer.WriteAsync(command.AssessmentId, command.ExpectedVersion, (round, assessment, now, by) => round.Submit(assessment.Id, command.Request.ToInput(), by, now), ct);
}

internal sealed class SupersedeAssessmentCommandHandler(AssessmentWriter writer) : ICommandHandler<SupersedeAssessmentCommand, VersionedRound>
{
    public Task<Result<VersionedRound>> Handle(SupersedeAssessmentCommand command, CancellationToken ct) =>
        writer.WriteAsync(
            command.AssessmentId,
            null,
            (round, assessment, now, by) => round.Supersede(assessment.Id, command.Request.ToInput(), command.Request.Reason ?? string.Empty, by, now),
            ct);
}
