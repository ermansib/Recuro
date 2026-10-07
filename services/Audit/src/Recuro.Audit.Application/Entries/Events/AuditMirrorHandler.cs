using System.Text.Json;
using Recuro.Audit.Application.Abstractions;
using Recuro.Audit.Domain.Entries;
using Recuro.BuildingBlocks.Application.IntegrationEvents;

namespace Recuro.Audit.Application.Entries.Events;

/// <summary>
/// Mirrors every integration event on the bus into the tenant's audit trail (RCU-AUD-001: "consumes
/// ALL domain events"). The event type is the action and the subject is the entity.
/// </summary>
public sealed class AuditMirrorHandler(IAuditChainWriter chain) : IIntegrationEventHandler<JsonElement>
{
    public async Task Handle(IntegrationEvent<JsonElement> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var (metadata, data) = (integrationEvent.Metadata, integrationEvent.Data);
        var record = new AuditRecord(
            ActorId: metadata.ActorId,
            ActorName: metadata.ActorName ?? metadata.ActorId ?? metadata.Source,
            ActorRole: metadata.ActorRole,
            Entity: Truncate(metadata.Subject, AuditLimits.EntityLength),
            Action: metadata.Type,
            Before: ReadString(data, "from"),
            After: Truncate(data.GetRawText(), AuditLimits.StateLength),
            Reason: ReadString(data, "reason"),
            ConfigVersion: ReadString(data, "configVersionId"),
            IpAddress: null,
            CorrelationId: metadata.CorrelationId,
            SourceEventId: metadata.Id,
            Source: metadata.Source,
            OccurredAt: metadata.Time);

        await chain.AppendAsync(record, ct);
    }

    private static string? ReadString(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
