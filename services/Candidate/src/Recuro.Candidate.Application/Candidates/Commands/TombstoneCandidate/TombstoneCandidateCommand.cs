using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Domain.Candidates;

namespace Recuro.Candidate.Application.Candidates.Commands.TombstoneCandidate;

/// <summary>
/// Saga compensation for intake services (Careers, Employee portal): a candidate they created is scrubbed
/// when the rest of the intake failed. Publishes candidate.purged like retention purge does.
/// </summary>
public sealed record TombstoneCandidateCommand(Guid Id) : ICommand;

internal sealed class TombstoneCandidateCommandHandler(
    ICandidateRepository candidates,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<TombstoneCandidateCommand>
{
    public async Task<Result> Handle(TombstoneCandidateCommand command, CancellationToken ct)
    {
        var candidate = await candidates.GetAsync(command.Id, ct);
        if (candidate is null)
        {
            return CandidateErrors.NotFound(command.Id);
        }

        var result = candidate.Tombstone(clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
