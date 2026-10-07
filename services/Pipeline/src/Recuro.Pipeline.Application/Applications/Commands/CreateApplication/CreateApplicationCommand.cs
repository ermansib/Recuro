using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Pipeline.Application.Abstractions;
using Recuro.Pipeline.Domain.Applications;
using ApplicationEntity = Recuro.Pipeline.Domain.Applications.Application;

namespace Recuro.Pipeline.Application.Applications.Commands.CreateApplication;

/// <summary>
/// RCU-PPL-001: a candidate applies to a requisition. The requisition must be open for sourcing and the
/// candidate must exist in the Candidate service. Starts at Sourced.
/// </summary>
public sealed record CreateApplicationCommand(string ReqId, string CandidateId, string Source, string? Note) : ICommand<ApplicationDto>;

internal sealed class CreateApplicationCommandValidator : AbstractValidator<CreateApplicationCommand>
{
    public CreateApplicationCommandValidator()
    {
        RuleFor(c => c.ReqId).NotEmpty().MaximumLength(PipelineLimits.ReqIdLength);
        RuleFor(c => c.CandidateId).NotEmpty().MaximumLength(PipelineLimits.CandidateIdLength);
        RuleFor(c => c.Source).Must(ApplicationSources.All.Contains)
            .WithMessage($"Source must be one of: {string.Join(", ", ApplicationSources.All)}.");
        RuleFor(c => c.Note).MaximumLength(PipelineLimits.NoteLength);
    }
}

internal sealed class CreateApplicationCommandHandler(
    IApplicationRepository applications,
    ISourcingGateRepository gates,
    IApplicationNumbers numbers,
    ICandidateDirectory candidates,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateApplicationCommand, ApplicationDto>
{
    public async Task<Result<ApplicationDto>> Handle(CreateApplicationCommand command, CancellationToken ct)
    {
        // RCU-MRF-006: demand precedes supply. The gate is built from the Requisition service's events.
        var gate = await gates.GetAsync(command.ReqId, ct);
        if (gate is not { IsOpen: true })
        {
            return ApplicationErrors.SourcingLocked(command.ReqId);
        }

        var existing = await applications.FindOpenAsync(command.ReqId, command.CandidateId, ct);
        if (existing is not null)
        {
            return ApplicationErrors.AlreadyApplied(existing.AppId);
        }

        if (!await candidates.ExistsAsync(command.CandidateId, ct))
        {
            return ApplicationErrors.UnknownCandidate(command.CandidateId);
        }

        var now = clock.GetUtcNow();
        var actor = Actors.From(caller);
        var appId = await numbers.NextAsync(now.Year, ct);
        var note = string.IsNullOrWhiteSpace(command.Note) ? $"Logged by {actor.Name}" : command.Note.Trim();
        var application = ApplicationEntity.Create(appId, command.ReqId, command.CandidateId, command.Source, note, actor, now);

        applications.Add(application);
        await unitOfWork.SaveChangesAsync(ct);
        return ApplicationDto.From(application);
    }
}
