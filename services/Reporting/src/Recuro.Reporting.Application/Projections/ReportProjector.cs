using System.Globalization;
using System.Text.Json;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Domain.Projections;
using IntegrationEventTypes = Recuro.BuildingBlocks.Application.IntegrationEvents.EventTypes;

namespace Recuro.Reporting.Application.Projections;

/// <summary>
/// Folds one logged event into the read models (RCU-RPT-001). Used by the live consumer and by replays,
/// so both give the same rows. Payloads are read field by field, tolerating additions (RCU-PLT-003), and
/// every update is idempotent, so replaying from any offset is safe.
/// </summary>
public sealed class ReportProjector(IProjectionStore store)
{
    /// <summary>The event types the projections read; the consumer subscribes to exactly these.</summary>
    public static readonly IReadOnlyList<string> EventTypes =
    [
        IntegrationEventTypes.Requisition.Submitted,
        IntegrationEventTypes.Requisition.Approved,
        IntegrationEventTypes.Requisition.Rejected,
        IntegrationEventTypes.Requisition.Cancelled,
        IntegrationEventTypes.Requisition.SourcingUnlocked,
        IntegrationEventTypes.Pipeline.ApplicationCreated,
        IntegrationEventTypes.Pipeline.StageChanged,
        IntegrationEventTypes.Pipeline.FinalRejected,
        IntegrationEventTypes.Interview.FeedbackSubmitted,
        IntegrationEventTypes.Interview.FeedbackOverdue,
        IntegrationEventTypes.Offer.Sent,
        IntegrationEventTypes.Offer.Accepted,
        IntegrationEventTypes.Offer.Declined,
        IntegrationEventTypes.Offer.Expired,
        IntegrationEventTypes.Offer.Withdrawn,
        IntegrationEventTypes.Candidate.Purged,
    ];

    public async Task ApplyAsync(string type, JsonElement data, DateTimeOffset occurredAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (data.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        switch (type)
        {
            case IntegrationEventTypes.Requisition.Submitted:
                if (Text(data, "reqId") is { } submitted)
                {
                    (await store.RequisitionAsync(submitted, ct)).Submitted(Text(data, "grade"), Text(data, "budgetStatus"), occurredAt);
                }

                break;
            case IntegrationEventTypes.Requisition.Approved:
                await RequisitionAsync(data, r => r.Approved(occurredAt), ct);
                break;
            case IntegrationEventTypes.Requisition.Rejected:
                await RequisitionAsync(data, r => r.Rejected(occurredAt), ct);
                break;
            case IntegrationEventTypes.Requisition.Cancelled:
                await RequisitionAsync(data, r => r.Cancelled(occurredAt), ct);
                break;
            case IntegrationEventTypes.Requisition.SourcingUnlocked:
                await RequisitionAsync(data, r => r.SourcingUnlocked(occurredAt), ct);
                break;
            case IntegrationEventTypes.Pipeline.ApplicationCreated:
                await ApplicationAsync(data, a => a.Created(occurredAt), ct);
                break;
            case IntegrationEventTypes.Pipeline.StageChanged:
                await StageChangedAsync(data, occurredAt, ct);
                break;
            case IntegrationEventTypes.Pipeline.FinalRejected:
                await ApplicationAsync(data, a => a.Rejected(occurredAt), ct);
                break;
            case IntegrationEventTypes.Interview.FeedbackSubmitted:
                await FeedbackSubmittedAsync(data, occurredAt, ct);
                break;
            case IntegrationEventTypes.Interview.FeedbackOverdue:
                await FeedbackOverdueAsync(data, occurredAt, ct);
                break;
            case IntegrationEventTypes.Offer.Sent:
                await ApplicationAsync(data, a => a.OfferSent(Instant(data, "sentAt") ?? occurredAt), ct);
                break;
            case IntegrationEventTypes.Offer.Accepted:
                await ApplicationAsync(data, a => a.OfferAccepted(occurredAt, Date(data, "joiningDate")), ct);
                break;
            case IntegrationEventTypes.Offer.Declined:
            case IntegrationEventTypes.Offer.Expired:
                await ApplicationAsync(data, a => a.OfferDeclined(occurredAt), ct);
                break;
            case IntegrationEventTypes.Offer.Withdrawn:
                await ApplicationAsync(data, a => a.OfferWithdrawn(occurredAt), ct);
                break;
            case IntegrationEventTypes.Candidate.Purged:
                if (Text(data, "candidateId") is { } candidateId)
                {
                    foreach (var application in await store.ApplicationsOfCandidateAsync(candidateId, ct))
                    {
                        application.ForgetCandidate();
                    }
                }

                break;
        }
    }

    private async Task StageChangedAsync(JsonElement data, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var at = Instant(data, "at") ?? occurredAt;
        var to = Text(data, "to");
        await ApplicationAsync(
            data,
            application =>
            {
                if (FunnelStages.FromPipeline(to) is { } stage)
                {
                    application.Reached(stage, at);
                }
                else if (to == "Rejected")
                {
                    application.Rejected(at);
                }
                else if (to == "Withdrawn")
                {
                    application.Withdrawn(at);
                }
            },
            ct);
    }

    private async Task FeedbackSubmittedAsync(JsonElement data, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (Text(data, "interviewId") is not { } interviewId || Text(data, "interviewerId") is not { } interviewerId)
        {
            return;
        }

        var fact = await store.FeedbackAsync(interviewId, interviewerId, Text(data, "appId"), ct);
        fact.Submitted(Instant(data, "submittedAt") ?? occurredAt, Bool(data, "withinSla") ?? false);
    }

    private async Task FeedbackOverdueAsync(JsonElement data, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (Text(data, "interviewId") is not { } interviewId
            || !data.TryGetProperty("pendingInterviewerIds", out var pending)
            || pending.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var at = Instant(data, "overdueAt") ?? occurredAt;
        foreach (var interviewer in pending.EnumerateArray())
        {
            if (interviewer.ValueKind == JsonValueKind.String && interviewer.GetString() is { Length: > 0 } interviewerId)
            {
                (await store.FeedbackAsync(interviewId, interviewerId, Text(data, "appId"), ct)).Overdue(at);
            }
        }
    }

    private async Task ApplicationAsync(JsonElement data, Action<ApplicationFact> apply, CancellationToken ct)
    {
        if (Text(data, "appId") is not { } appId)
        {
            return;
        }

        var application = await store.ApplicationAsync(appId, ct);
        application.Identify(Text(data, "reqId"), Text(data, "candidateId"), Text(data, "source"));
        apply(application);
    }

    private async Task RequisitionAsync(JsonElement data, Action<RequisitionFact> apply, CancellationToken ct)
    {
        if (Text(data, "reqId") is { } reqId)
        {
            apply(await store.RequisitionAsync(reqId, ct));
        }
    }

    private static string? Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static bool? Bool(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static DateTimeOffset? Instant(JsonElement data, string name) =>
        Text(data, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at.ToUniversalTime()
            : null;

    private static DateOnly? Date(JsonElement data, string name) =>
        Text(data, name) is { } text && DateOnly.TryParse(text.Length >= 10 ? text[..10] : text, CultureInfo.InvariantCulture, out var day)
            ? day
            : null;
}
