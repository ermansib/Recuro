using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Onboarding.Application.Abstractions;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Application.Cases.Commands;

/// <summary>RCU-ONB-002: HR ticks or unticks one Day-1 item, with an optional remark. 100% → Day-1 Ready.</summary>
public sealed record SetChecklistItemCommand(string CaseRef, string ItemKey, bool Done, string? Remarks) : ICommand<OnboardingCaseDto>;

internal sealed class SetChecklistItemCommandValidator : AbstractValidator<SetChecklistItemCommand>
{
    public SetChecklistItemCommandValidator() => RuleFor(c => c.Remarks).MaximumLength(OnboardingLimits.RemarksLength);
}

internal sealed class SetChecklistItemCommandHandler(
    IOnboardingCaseRepository cases,
    CaseViews views,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetChecklistItemCommand, OnboardingCaseDto>
{
    public async Task<Result<OnboardingCaseDto>> Handle(SetChecklistItemCommand command, CancellationToken ct)
    {
        var onboardingCase = await cases.FindAsync(command.CaseRef, ct);
        if (onboardingCase is null)
        {
            return OnboardingErrors.NotFound(command.CaseRef);
        }

        var set = onboardingCase.SetChecklistItem(command.ItemKey, command.Done, command.Remarks, Actors.From(caller), clock.GetUtcNow());
        if (set.IsFailure)
        {
            return set.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await views.ToDtoAsync(onboardingCase, ct);
    }
}

/// <summary>Records the reporting manager (who gets probation reminders) and the buddy.</summary>
public sealed record AssignPeopleCommand(string CaseRef, string? ReportingManagerId, string? ReportingManager, string? Buddy) : ICommand<OnboardingCaseDto>;

internal sealed class AssignPeopleCommandValidator : AbstractValidator<AssignPeopleCommand>
{
    public AssignPeopleCommandValidator()
    {
        RuleFor(c => c.ReportingManagerId).MaximumLength(OnboardingLimits.PersonIdLength);
        RuleFor(c => c.ReportingManager).MaximumLength(OnboardingLimits.ActorLength);
        RuleFor(c => c.Buddy).MaximumLength(OnboardingLimits.ActorLength);
    }
}

internal sealed class AssignPeopleCommandHandler(IOnboardingCaseRepository cases, CaseViews views, IUnitOfWork unitOfWork)
    : ICommandHandler<AssignPeopleCommand, OnboardingCaseDto>
{
    public async Task<Result<OnboardingCaseDto>> Handle(AssignPeopleCommand command, CancellationToken ct)
    {
        var onboardingCase = await cases.FindAsync(command.CaseRef, ct);
        if (onboardingCase is null)
        {
            return OnboardingErrors.NotFound(command.CaseRef);
        }

        var assigned = onboardingCase.Assign(command.ReportingManagerId, command.ReportingManager, command.Buddy);
        if (assigned.IsFailure)
        {
            return assigned.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await views.ToDtoAsync(onboardingCase, ct);
    }
}

/// <summary>RCU-ONB-004: completes a touchpoint or probation milestone with notes.</summary>
public sealed record CompleteMilestoneCommand(string CaseRef, Guid MilestoneId, string? Notes) : ICommand<OnboardingCaseDto>;

internal sealed class CompleteMilestoneCommandValidator : AbstractValidator<CompleteMilestoneCommand>
{
    public CompleteMilestoneCommandValidator() => RuleFor(c => c.Notes).MaximumLength(OnboardingLimits.NoteLength);
}

internal sealed class CompleteMilestoneCommandHandler(
    IOnboardingCaseRepository cases,
    CaseViews views,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CompleteMilestoneCommand, OnboardingCaseDto>
{
    public async Task<Result<OnboardingCaseDto>> Handle(CompleteMilestoneCommand command, CancellationToken ct)
    {
        var onboardingCase = await cases.FindAsync(command.CaseRef, ct);
        if (onboardingCase is null)
        {
            return OnboardingErrors.NotFound(command.CaseRef);
        }

        var completed = onboardingCase.CompleteMilestone(command.MilestoneId, command.Notes, Actors.From(caller), clock.GetUtcNow());
        if (completed.IsFailure)
        {
            return completed.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await views.ToDtoAsync(onboardingCase, ct);
    }
}
