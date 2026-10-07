using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Candidate.Application.Abstractions;

namespace Recuro.Candidate.Application.Candidates.Commands.PurgeCandidates;

/// <summary>
/// RCU-CND-003: anonymises the current tenant's candidates whose retention period is over. With
/// <see cref="DryRun"/> nothing changes and the result reports the impact. The nightly job runs it per tenant.
/// </summary>
public sealed record PurgeCandidatesCommand(bool DryRun, int? Limit) : ICommand<PurgeReportDto>;

/// <summary>Which candidates were (or would be) purged. Ids only: no personal data.</summary>
public sealed record PurgeReportDto(bool DryRun, int Count, IReadOnlyList<string> CandidateIds);

internal sealed class PurgeCandidatesCommandValidator : AbstractValidator<PurgeCandidatesCommand>
{
    public const int MaxBatch = 1000;

    public PurgeCandidatesCommandValidator() => RuleFor(c => c.Limit).InclusiveBetween(1, MaxBatch);
}

internal sealed class PurgeCandidatesCommandHandler(
    ICandidateRepository candidates,
    IResumeStore resumes,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<PurgeCandidatesCommand, PurgeReportDto>
{
    public async Task<Result<PurgeReportDto>> Handle(PurgeCandidatesCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await candidates.ListDueForPurgeAsync(
            DateOnly.FromDateTime(now.UtcDateTime),
            command.Limit ?? PurgeCandidatesCommandValidator.MaxBatch,
            ct);
        var ids = due.Select(c => c.Id.ToString()).ToList();
        if (command.DryRun || due.Count == 0)
        {
            return new PurgeReportDto(command.DryRun, ids.Count, ids);
        }

        var files = due.Select(c => c.Resume?.StorageKey).OfType<string>().ToList();
        foreach (var candidate in due)
        {
            candidate.Purge(now);
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Files go only after the scrub committed; a leftover file is encrypted and unreferenced.
        foreach (var key in files)
        {
            await resumes.DeleteAsync(key, ct);
        }

        return new PurgeReportDto(false, ids.Count, ids);
    }
}
