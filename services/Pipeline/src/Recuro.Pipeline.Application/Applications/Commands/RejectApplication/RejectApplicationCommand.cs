using FluentValidation;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Pipeline.Application.Abstractions;
using Recuro.Pipeline.Domain.Applications;

namespace Recuro.Pipeline.Application.Applications.Commands.RejectApplication;

/// <summary>
/// RCU-PPL-003 / RCU-PIP-005: final rejection with a mandatory reason. Sets the regret deadline
/// (+3 working days) and the retention date; Notification sends the regret email from the event.
/// </summary>
public sealed record RejectApplicationCommand(string AppId, string Reason) : ICommand<ApplicationDto>;

internal sealed class RejectApplicationCommandValidator : AbstractValidator<RejectApplicationCommand>
{
    public RejectApplicationCommandValidator()
    {
        RuleFor(c => c.Reason).NotEmpty().WithErrorCode("reason_required").WithMessage("A rejection reason is mandatory.")
            .MaximumLength(PipelineLimits.ReasonLength);
    }
}

internal sealed class RejectApplicationCommandHandler(
    IApplicationRepository applications,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    IOptions<PipelineOptions> options,
    TimeProvider clock) : ICommandHandler<RejectApplicationCommand, ApplicationDto>
{
    public async Task<Result<ApplicationDto>> Handle(RejectApplicationCommand command, CancellationToken ct)
    {
        var application = await applications.GetAsync(command.AppId, ct);
        if (application is null)
        {
            return ApplicationErrors.NotFound(command.AppId);
        }

        var rules = options.Value;
        var rejected = application.Reject(command.Reason, Actors.From(caller), clock.GetUtcNow(), rules.RegretWorkingDays, rules.RetentionDays);
        if (rejected.IsFailure)
        {
            return rejected.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return ApplicationDto.From(application);
    }
}
