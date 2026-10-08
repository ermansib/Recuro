using Recuro.Bgv.Domain.Cases;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;

namespace Recuro.Bgv.Application.Cases.Events;

// Payloads carry ids, never personal data (services/contracts/events/bgv.*.schema.json). The catalog's
// common fields are caseId, appId, checkType, status and vendorRef (RCU-BKD-001 §5).

/// <summary><c>bgv.case.initiated.v1</c>.</summary>
public sealed record CaseInitiatedPayload(
    string CaseId,
    string AppId,
    string ReqId,
    string CandidateId,
    string VendorId,
    string VendorRef,
    string Scope,
    string Grade,
    IReadOnlyList<string> Checks,
    IReadOnlyList<string> NotApplicable,
    DateTimeOffset DueAt,
    string ConfigVersionId);

/// <summary><c>bgv.check.updated.v1</c>; Vendor rolls these up into SLA metrics (RCU-VND-004).</summary>
public sealed record CheckUpdatedPayload(
    string CaseId,
    string AppId,
    string VendorId,
    string VendorRef,
    string CheckType,
    string From,
    string Status,
    string Actor,
    DateTimeOffset At);

/// <summary><c>bgv.adverse.flagged.v1</c>: Pipeline holds the application, Offer locks the release.</summary>
public sealed record AdverseFlaggedPayload(
    string CaseId,
    string AppId,
    string ReqId,
    string VendorId,
    string VendorRef,
    string CheckType,
    string Status,
    string Action);

/// <summary><c>bgv.cleared.v1</c>, once per case; <c>onTime</c> feeds the cleared-on-time KPI (RCU-BGV-005).</summary>
public sealed record CaseClearedPayload(
    string CaseId,
    string AppId,
    string ReqId,
    string VendorId,
    string VendorRef,
    string Status,
    DateTimeOffset ClearedAt,
    DateTimeOffset DueAt,
    bool OnTime);

/// <summary><c>bgv.resolved.v1</c>: <c>ResolvedCleared</c> resumes the hire, <c>ResolvedAdverse</c> rescinds it.</summary>
public sealed record CaseResolvedPayload(
    string CaseId,
    string AppId,
    string ReqId,
    string VendorId,
    string CheckType,
    string Status,
    string Decision,
    string DecidedBy);

/// <summary>Turns domain events into integration events through the outbox, in the same transaction as the change.</summary>
internal sealed class PublishBgvEvents(IIntegrationEventPublisher publisher)
    : IDomainEventHandler<CaseInitiatedDomainEvent>,
      IDomainEventHandler<CheckUpdatedDomainEvent>,
      IDomainEventHandler<AdverseFlaggedDomainEvent>,
      IDomainEventHandler<CaseClearedDomainEvent>,
      IDomainEventHandler<AdverseResolvedDomainEvent>
{
    public const string ResolvedCleared = "ResolvedCleared";
    public const string ResolvedAdverse = "ResolvedAdverse";

    public Task Handle(CaseInitiatedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var c = domainEvent.Case;
        publisher.Publish(
            EventTypes.Bgv.CaseInitiated,
            Subject(c),
            new CaseInitiatedPayload(
                c.Id.ToString(),
                c.AppId,
                c.ReqId,
                c.CandidateId,
                c.VendorId,
                c.VendorCaseRef,
                c.Scope.ToString(),
                c.Grade,
                c.Checks.Where(x => x.Status != CheckStatus.NotApplicable).Select(x => x.Type).ToList(),
                c.Checks.Where(x => x.Status == CheckStatus.NotApplicable).Select(x => x.Type).ToList(),
                c.DueAt,
                c.MatrixVersionId));
        return Task.CompletedTask;
    }

    public Task Handle(CheckUpdatedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var c = domainEvent.Case;
        publisher.Publish(
            EventTypes.Bgv.CheckUpdated,
            Subject(c),
            new CheckUpdatedPayload(
                c.Id.ToString(),
                c.AppId,
                c.VendorId,
                c.VendorCaseRef,
                domainEvent.CheckType,
                domainEvent.From.ToString(),
                domainEvent.To.ToString(),
                domainEvent.Actor.Id ?? domainEvent.Actor.Name,
                domainEvent.At));
        return Task.CompletedTask;
    }

    public Task Handle(AdverseFlaggedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var c = domainEvent.Case;
        publisher.Publish(
            EventTypes.Bgv.AdverseFlagged,
            Subject(c),
            new AdverseFlaggedPayload(c.Id.ToString(), c.AppId, c.ReqId, c.VendorId, c.VendorCaseRef, domainEvent.CheckType, nameof(CheckStatus.Flagged), domainEvent.Action.ToString()));
        return Task.CompletedTask;
    }

    public Task Handle(CaseClearedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var c = domainEvent.Case;
        publisher.Publish(
            EventTypes.Bgv.Cleared,
            Subject(c),
            new CaseClearedPayload(c.Id.ToString(), c.AppId, c.ReqId, c.VendorId, c.VendorCaseRef, nameof(CaseStatus.Cleared), c.ClearedAt!.Value, c.DueAt, domainEvent.OnTime));
        return Task.CompletedTask;
    }

    public Task Handle(AdverseResolvedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var c = domainEvent.Case;
        var cleared = domainEvent.Outcome == AdverseOutcome.Override;
        publisher.Publish(
            EventTypes.Bgv.Resolved,
            Subject(c),
            new CaseResolvedPayload(
                c.Id.ToString(),
                c.AppId,
                c.ReqId,
                c.VendorId,
                c.Adverse?.Check ?? string.Empty,
                cleared ? ResolvedCleared : ResolvedAdverse,
                domainEvent.Outcome.ToString().ToLowerInvariant(),
                domainEvent.Actor.Id ?? domainEvent.Actor.Name));
        return Task.CompletedTask;
    }

    private static string Subject(BgvCase bgvCase) => $"BgvCase/{bgvCase.Id}";
}
