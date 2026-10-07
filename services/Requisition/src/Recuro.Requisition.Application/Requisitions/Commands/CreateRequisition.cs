using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.Requisitions.Commands;

/// <summary>
/// RCU-REQ-001/002: HR-TA raises an MRF. By default it is submitted straight away, like the wizard's
/// "Submit" (frontend <c>createRequisition</c>); with <see cref="SaveAsDraft"/> it is only saved.
/// </summary>
public sealed record CreateRequisitionCommand(RequisitionInput Input, bool SaveAsDraft) : ICommand<VersionedRequisition>;

internal sealed class CreateRequisitionCommandValidator : AbstractValidator<CreateRequisitionCommand>
{
    public CreateRequisitionCommandValidator() =>
        RuleFor(c => c.Input).NotNull().SetValidator(new RequisitionInputValidator());
}

internal sealed class CreateRequisitionCommandHandler(
    IRequisitionRepository requisitions,
    RequisitionSubmission submission,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<CreateRequisitionCommand, VersionedRequisition>
{
    public async Task<Result<VersionedRequisition>> Handle(CreateRequisitionCommand command, CancellationToken ct)
    {
        var details = command.Input.ToDetails();
        var draft = ManpowerRequisition.CreateDraft(details, user.UserId ?? string.Empty, user.Name ?? user.UserId ?? string.Empty, clock.GetUtcNow());

        // Check completeness before anything is saved, so a failed submit leaves no stray draft behind.
        if (!command.SaveAsDraft)
        {
            var ready = draft.EnsureCanSubmit();
            if (ready.IsFailure)
            {
                return ready.Error!;
            }
        }

        requisitions.Add(draft);
        await unitOfWork.SaveChangesAsync(ct);

        if (!command.SaveAsDraft)
        {
            var submitted = await submission.SubmitAsync(draft, ct);
            if (submitted.IsFailure)
            {
                return submitted.Error!;
            }
        }

        return new VersionedRequisition(RequisitionDto.From(draft, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), draft.Version);
    }
}
