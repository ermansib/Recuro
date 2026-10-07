using Recuro.Audit.Application.Abstractions;
using Recuro.Audit.Domain.Entries;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Audit.Application.Entries.Commands.RecordAuditEntry;

internal sealed class RecordAuditEntryCommandHandler(
    IAuditChainWriter chain,
    ICurrentUser caller,
    ICorrelationContext correlation,
    TimeProvider clock) : ICommandHandler<RecordAuditEntryCommand, AuditEventDto>
{
    /// <summary>CloudEvents-style source for entries sent straight to the API rather than over the bus.</summary>
    public const string DirectIngestSource = "/services/audit/ingest";

    public async Task<Result<AuditEventDto>> Handle(RecordAuditEntryCommand command, CancellationToken ct)
    {
        var record = new AuditRecord(
            ActorId: command.ActorId ?? caller.UserId,
            ActorName: command.ActorName ?? caller.Name ?? caller.UserId ?? "unknown",
            ActorRole: command.ActorRole ?? caller.Roles.FirstOrDefault(),
            Entity: command.Entity,
            Action: command.Action,
            Before: command.Before,
            After: command.After,
            Reason: command.Reason,
            ConfigVersion: command.ConfigVersion,
            IpAddress: command.IpAddress,
            CorrelationId: correlation.CorrelationId,
            SourceEventId: null,
            Source: DirectIngestSource,
            OccurredAt: command.OccurredAt ?? clock.GetUtcNow());

        var entry = await chain.AppendAsync(record, ct);
        return AuditEventDto.From(entry);
    }
}
