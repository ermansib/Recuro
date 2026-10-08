using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.Requisitions.Commands;

/// <summary>
/// RCU-REQ-008: HR Head cancels a requisition with a reason. Open applications hear about it via MRFCancelled.
/// The FRD §6 table allows it from Draft and from Approved onwards; a pending approval is decided first.
/// </summary>
public sealed record CancelRequisitionCommand(string ReqId, string Reason) : ICommand<VersionedRequisition>;

internal sealed class CancelRequisitionCommandValidator : AbstractValidator<CancelRequisitionCommand>
{
    public CancelRequisitionCommandValidator() =>
        RuleFor(c => c.Reason).MaximumLength(RequisitionLimits.LongText);
}

internal sealed class CancelRequisitionCommandHandler(
    IRequisitionRepository requisitions,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CancelRequisitionCommand, VersionedRequisition>
{
    public async Task<Result<VersionedRequisition>> Handle(CancelRequisitionCommand command, CancellationToken ct)
    {
        var requisition = await requisitions.GetByReqIdAsync(command.ReqId, ct);
        if (requisition is null)
        {
            return RequisitionErrors.NotFound(command.ReqId);
        }

        var cancelled = requisition.Cancel(command.Reason, clock.GetUtcNow());
        if (cancelled.IsFailure)
        {
            return cancelled.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new VersionedRequisition(RequisitionDto.From(requisition, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), requisition.Version);
    }
}
