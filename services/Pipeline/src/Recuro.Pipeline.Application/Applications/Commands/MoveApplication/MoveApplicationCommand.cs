using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Pipeline.Application.Abstractions;
using Recuro.Pipeline.Domain.Applications;

namespace Recuro.Pipeline.Application.Applications.Commands.MoveApplication;

/// <summary>RCU-PPL-002: move an application to another stage (frontend <c>moveApplication</c>). Illegal moves are 409.</summary>
public sealed record MoveApplicationCommand(string AppId, string To) : ICommand<ApplicationDto>;

internal sealed class MoveApplicationCommandValidator : AbstractValidator<MoveApplicationCommand>
{
    public MoveApplicationCommandValidator()
    {
        RuleFor(c => c.To).IsEnumName(typeof(ApplicationStage), caseSensitive: true)
            .WithMessage($"Stage must be one of: {string.Join(", ", Enum.GetNames<ApplicationStage>())}.");
    }
}

internal sealed class MoveApplicationCommandHandler(
    IApplicationRepository applications,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<MoveApplicationCommand, ApplicationDto>
{
    public async Task<Result<ApplicationDto>> Handle(MoveApplicationCommand command, CancellationToken ct)
    {
        var application = await applications.GetAsync(command.AppId, ct);
        if (application is null)
        {
            return ApplicationErrors.NotFound(command.AppId);
        }

        var moved = application.MoveTo(Enum.Parse<ApplicationStage>(command.To), Actors.From(caller), clock.GetUtcNow());
        if (moved.IsFailure)
        {
            return moved.Error!;
        }

        // Optimistic concurrency (row version): two people moving the same card at once → the second gets 409.
        await unitOfWork.SaveChangesAsync(ct);
        return ApplicationDto.From(application);
    }
}
