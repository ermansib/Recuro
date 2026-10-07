using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Domain.Candidates;

namespace Recuro.Candidate.Application.Candidates.Commands.SetLegalHold;

/// <summary>RCU-CND-003: pins a candidate so retention purge skips them (litigation, grievance), or releases the pin.</summary>
public sealed record SetLegalHoldCommand(Guid Id, bool OnHold, string? Reason) : ICommand;

internal sealed class SetLegalHoldCommandValidator : AbstractValidator<SetLegalHoldCommand>
{
    public SetLegalHoldCommandValidator()
    {
        RuleFor(c => c.Reason).NotEmpty().When(c => c.OnHold).WithMessage("A legal hold needs a reason.");
        RuleFor(c => c.Reason).MaximumLength(CandidateLimits.ReasonLength);
    }
}

internal sealed class SetLegalHoldCommandHandler(ICandidateRepository candidates, IUnitOfWork unitOfWork) : ICommandHandler<SetLegalHoldCommand>
{
    public async Task<Result> Handle(SetLegalHoldCommand command, CancellationToken ct)
    {
        var candidate = await candidates.GetAsync(command.Id, ct);
        if (candidate is null)
        {
            return CandidateErrors.NotFound(command.Id);
        }

        if (command.OnHold)
        {
            candidate.PlaceLegalHold(command.Reason!);
        }
        else
        {
            candidate.ReleaseLegalHold();
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
