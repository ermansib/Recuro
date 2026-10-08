using Recuro.BuildingBlocks.Domain;

namespace Recuro.Offer.Domain.Offers;

public sealed record OfferSubmitted(JobOffer Offer) : IDomainEvent;

public sealed record OfferApproved(JobOffer Offer, string ApprovedBy) : IDomainEvent;

public sealed record OfferSent(JobOffer Offer) : IDomainEvent;

public sealed record OfferChaseDue(JobOffer Offer) : IDomainEvent;

public sealed record OfferAccepted(JobOffer Offer) : IDomainEvent;

public sealed record OfferDeclined(JobOffer Offer, string? Reason) : IDomainEvent;

public sealed record OfferExpired(JobOffer Offer) : IDomainEvent;

public sealed record OfferWithdrawn(JobOffer Offer, OfferState From, string Reason) : IDomainEvent;
