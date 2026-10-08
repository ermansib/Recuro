using System.Text.Json;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Application.Projections;
using Recuro.Reporting.Domain.Events;

namespace Recuro.Reporting.Application.Events;

/// <summary>
/// RCU-RPT-001 checkpointed consumer: logs the event, folds it into the projections and advances the
/// checkpoint, all in the consumer's transaction with its inbox row. Reports never query another
/// service's database or API for this data.
/// </summary>
public sealed class ReportEventHandler(
    IEventLog log,
    IProjectionStore projections,
    ReportProjector projector,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IIntegrationEventHandler<JsonElement>
{
    public const string Projection = "kpi";

    public async Task Handle(IntegrationEvent<JsonElement> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var (metadata, data) = (integrationEvent.Metadata, integrationEvent.Data);
        await projections.LockAsync(ct);
        if (await log.ContainsAsync(metadata.Id, ct))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var entry = ReportEvent.Record(metadata.Id, metadata.Type, metadata.Subject, metadata.Time, now, data.GetRawText());
        log.Append(entry);
        await projector.ApplyAsync(metadata.Type, data, metadata.Time, ct);

        // The first save assigns the log offset; the consumer's own save then commits the checkpoint.
        await unitOfWork.SaveChangesAsync(ct);
        (await projections.CheckpointAsync(ct)).Advance(entry.Sequence, now);
    }
}

/// <summary>Notification's dispatch report for an email (<c>notification.email.dispatched/failed.v1</c>).</summary>
public sealed record EmailDispatchedPayload(string TemplateKey, string Status, string? SourceEventId);

/// <summary>
/// RCU-RPT-003 "delivery logged": Notification reports each pack email it sends, quoting the
/// <c>reporting.pack.ready</c> event that asked for it; the archive records the outcome.
/// </summary>
public sealed class PackDeliveryHandler(IPackArchive packs, TimeProvider clock) : IIntegrationEventHandler<EmailDispatchedPayload>
{
    public async Task Handle(IntegrationEvent<EmailDispatchedPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!Guid.TryParse(integrationEvent.Data.SourceEventId, out var readyEventId))
        {
            return;
        }

        var pack = await packs.FindByReadyEventAsync(readyEventId, ct);
        pack?.Delivered(string.Equals(integrationEvent.Data.Status, "Sent", StringComparison.Ordinal), clock.GetUtcNow());
    }
}
