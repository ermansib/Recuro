using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.Requisitions.Commands;

/// <summary>RCU-REQ-002: submit a saved draft for approval.</summary>
public sealed record SubmitRequisitionCommand(string ReqId, uint? ExpectedVersion) : ICommand<VersionedRequisition>;

internal sealed class SubmitRequisitionCommandHandler(
    IRequisitionRepository requisitions,
    RequisitionSubmission submission,
    TimeProvider clock) : ICommandHandler<SubmitRequisitionCommand, VersionedRequisition>
{
    public async Task<Result<VersionedRequisition>> Handle(SubmitRequisitionCommand command, CancellationToken ct)
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

        var submitted = await submission.SubmitAsync(requisition, ct);
        if (submitted.IsFailure)
        {
            return submitted.Error!;
        }

        return new VersionedRequisition(RequisitionDto.From(requisition, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), requisition.Version);
    }
}
