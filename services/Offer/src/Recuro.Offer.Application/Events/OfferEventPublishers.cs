using System.Globalization;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Events;

// Domain events → integration events, written to the outbox in the same transaction as the change
// (RCU-PLT-002). Payloads follow services/contracts/events/offer.*.schema.json. Ids only: no candidate
// name or CTC leaves this service on the bus.

/// <summary><c>offer.submitted.v1</c>: the Annexure D route result, with the pinned Config version (RCU-OFR-002).</summary>
internal sealed record OfferSubmittedPayload(
    Guid OfferId,
    string AppId,
    string ReqId,
    string Grade,
    bool WithinBand,
    string ApproverRole,
    string Route,
    string ConfigVersionId);

internal sealed record OfferApprovedPayload(Guid OfferId, string AppId, string ReqId, string ApprovedBy, int LetterVersion);

internal sealed record OfferSentPayload(Guid OfferId, string AppId, string ReqId, string CandidateId, DateTimeOffset SentAt, DateTimeOffset ExpiresAt, bool Conditional);

/// <summary><c>offer.chase_due.v1</c> (schema in contracts/events).</summary>
internal sealed record OfferChaseDuePayload(Guid OfferId, string AppId, string ReqId, string CandidateId, DateTimeOffset SentAt, int ChaseNumber, DateTimeOffset ExpiresAt);

/// <summary><c>offer.accepted.v1</c>: Pipeline moves the application to PreBoarding; <c>joiningDate</c> feeds its "Joining ≤ 30d" tile.</summary>
internal sealed record OfferAcceptedPayload(Guid OfferId, string AppId, string ReqId, string CandidateId, string JoiningDate);

internal sealed record OfferDeclinedPayload(Guid OfferId, string AppId, string ReqId, string CandidateId);

internal sealed record OfferExpiredPayload(Guid OfferId, string AppId, string ReqId, string CandidateId, DateTimeOffset ExpiresAt);

internal sealed record OfferWithdrawnPayload(Guid OfferId, string AppId, string ReqId, string CandidateId, OfferState From, string Reason);

internal sealed class OfferEventPublishers(IIntegrationEventPublisher publisher)
    : IDomainEventHandler<OfferSubmitted>,
      IDomainEventHandler<OfferApproved>,
      IDomainEventHandler<OfferSent>,
      IDomainEventHandler<OfferChaseDue>,
      IDomainEventHandler<OfferAccepted>,
      IDomainEventHandler<OfferDeclined>,
      IDomainEventHandler<OfferExpired>,
      IDomainEventHandler<OfferWithdrawn>
{
    public Task Handle(OfferSubmitted domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var o = domainEvent.Offer;
        var route = o.Route!;
        return Publish(EventTypes.Offer.Submitted, o, new OfferSubmittedPayload(o.Id, o.AppId, o.ReqId, o.Grade, route.WithinBand, route.ApproverRole, route.Label, route.ConfigVersionId));
    }

    public Task Handle(OfferApproved domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var o = domainEvent.Offer;
        return Publish(EventTypes.Offer.Approved, o, new OfferApprovedPayload(o.Id, o.AppId, o.ReqId, domainEvent.ApprovedBy, o.LetterVersion));
    }

    public Task Handle(OfferSent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var o = domainEvent.Offer;
        return Publish(EventTypes.Offer.Sent, o, new OfferSentPayload(o.Id, o.AppId, o.ReqId, o.CandidateId, o.SentAt!.Value, o.ExpiresAt!.Value, o.Conditional));
    }

    public Task Handle(OfferChaseDue domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var o = domainEvent.Offer;
        return Publish(EventTypes.Offer.ChaseDue, o, new OfferChaseDuePayload(o.Id, o.AppId, o.ReqId, o.CandidateId, o.SentAt!.Value, o.ChaseCount, o.ExpiresAt!.Value));
    }

    public Task Handle(OfferAccepted domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var o = domainEvent.Offer;
        return Publish(EventTypes.Offer.Accepted, o, new OfferAcceptedPayload(o.Id, o.AppId, o.ReqId, o.CandidateId, o.JoiningDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    public Task Handle(OfferDeclined domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var o = domainEvent.Offer;
        return Publish(EventTypes.Offer.Declined, o, new OfferDeclinedPayload(o.Id, o.AppId, o.ReqId, o.CandidateId));
    }

    public Task Handle(OfferExpired domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var o = domainEvent.Offer;
        return Publish(EventTypes.Offer.Expired, o, new OfferExpiredPayload(o.Id, o.AppId, o.ReqId, o.CandidateId, o.ExpiresAt!.Value));
    }

    public Task Handle(OfferWithdrawn domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var o = domainEvent.Offer;
        return Publish(EventTypes.Offer.Withdrawn, o, new OfferWithdrawnPayload(o.Id, o.AppId, o.ReqId, o.CandidateId, domainEvent.From, domainEvent.Reason));
    }

    private Task Publish<T>(string type, JobOffer offer, T payload)
        where T : notnull
    {
        publisher.Publish(type, $"Offer/{offer.Id}", payload);
        return Task.CompletedTask;
    }
}
