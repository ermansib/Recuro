using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Onboarding.Application.Abstractions;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Application.Cases.Commands;

/// <summary>
/// RCU-ONB-005: confirm or extend at the end of probation, by the joiner's department head (HR Head
/// when the department has no head). Confirm issues the letter and publishes
/// <c>onboarding.employee.confirmed</c>; Extend needs a reason and 1–6 months and starts a new cycle.
/// </summary>
public sealed record DecideProbationCommand(string CaseRef, string Decision, string? Reason, int? ExtendByMonths) : ICommand<OnboardingCaseDto>;

internal sealed class DecideProbationCommandValidator : AbstractValidator<DecideProbationCommand>
{
    public DecideProbationCommandValidator()
    {
        RuleFor(c => c.Decision)
            .Must(d => Enum.TryParse<ProbationOutcome>(d, ignoreCase: true, out var outcome) && Enum.IsDefined(outcome) && !int.TryParse(d, out _))
            .WithErrorCode("decision")
            .WithMessage("decision must be 'confirm' or 'extend'.");
        RuleFor(c => c.Reason).MaximumLength(OnboardingLimits.ReasonLength);
    }
}

internal sealed class DecideProbationCommandHandler(
    IOnboardingCaseRepository cases,
    CaseViews views,
    ProbationDecider decider,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<DecideProbationCommand, OnboardingCaseDto>
{
    public async Task<Result<OnboardingCaseDto>> Handle(DecideProbationCommand command, CancellationToken ct)
    {
        var onboardingCase = await cases.FindAsync(command.CaseRef, ct);
        if (onboardingCase is null)
        {
            return OnboardingErrors.NotFound(command.CaseRef);
        }

        // The joiner's department head decides; HR Head only when the department has none (RCU-ONB-005).
        var actor = ProbationDecider.Authorize(await decider.RouteAsync(onboardingCase, ct), caller);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        var now = clock.GetUtcNow();
        var decided = onboardingCase.DecideProbation(
            Enum.Parse<ProbationOutcome>(command.Decision, ignoreCase: true),
            command.Reason,
            command.ExtendByMonths,
            await views.BgvStatusAsync(onboardingCase.AppId, ct),
            actor.Value,
            DateOnly.FromDateTime(now.UtcDateTime),
            now);
        if (decided.IsFailure)
        {
            return decided.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await views.ToDtoAsync(onboardingCase, ct);
    }
}

/// <summary>
/// RCU-ONB-001/004 for the current tenant: raises the milestones whose date has come and opens the
/// IT/Admin tickets that are due. Returns how many steps moved. Run by the scheduler, one replica at a time.
/// </summary>
public sealed record ScanMilestonesCommand : ICommand<int>
{
    public const int BatchSize = 200;
}

internal sealed class ScanMilestonesCommandHandler(
    IOnboardingCaseRepository cases,
    IProvisioningAdapter provisioning,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ScanMilestonesCommand, int>
{
    public async Task<Result<int>> Handle(ScanMilestonesCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var moved = 0;
        foreach (var onboardingCase in await cases.ListWithDueMilestonesAsync(today, ScanMilestonesCommand.BatchSize, ct))
        {
            moved += onboardingCase.RaiseDueMilestones(today, now).Count;
            if (onboardingCase.ProvisioningDue(today) is { } step)
            {
                var ticket = await provisioning.OpenTicketAsync(
                    new ProvisioningRequest(onboardingCase.Id, onboardingCase.AppId, onboardingCase.ReqId, onboardingCase.CandidateId, onboardingCase.JoiningDate, onboardingCase.ReportingManagerId),
                    ct);
                if (onboardingCase.RecordProvisioning(step.Id, ticket, now).IsSuccess)
                {
                    moved++;
                }
            }
        }

        if (moved > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return moved;
    }
}
