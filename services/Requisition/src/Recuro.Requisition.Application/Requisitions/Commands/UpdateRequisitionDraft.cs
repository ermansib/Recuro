using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.Requisitions.Commands;

/// <summary>RCU-REQ-001: draft autosave with optimistic locking (If-Match / ETag).</summary>
public sealed record UpdateRequisitionDraftCommand(string ReqId, uint? ExpectedVersion, RequisitionInput Input) : ICommand<VersionedRequisition>;

internal sealed class UpdateRequisitionDraftCommandValidator : AbstractValidator<UpdateRequisitionDraftCommand>
{
    public UpdateRequisitionDraftCommandValidator() =>
        RuleFor(c => c.Input).NotNull().SetValidator(new RequisitionInputValidator());
}

internal sealed class UpdateRequisitionDraftCommandHandler(
    IRequisitionRepository requisitions,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpdateRequisitionDraftCommand, VersionedRequisition>
{
    public async Task<Result<VersionedRequisition>> Handle(UpdateRequisitionDraftCommand command, CancellationToken ct)
    {
        var requisition = await requisitions.GetByReqIdAsync(command.ReqId, ct);
        if (requisition is null)
        {
            return RequisitionErrors.NotFound(command.ReqId);
        }

        if (command.ExpectedVersion is { } expected && expected != requisition.Version)
        {
            return RequisitionErrors.StaleVersion(requisition.ReqId);
        }

        var updated = requisition.UpdateDraft(command.Input.ToDetails());
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new VersionedRequisition(RequisitionDto.From(requisition, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), requisition.Version);
    }
}
