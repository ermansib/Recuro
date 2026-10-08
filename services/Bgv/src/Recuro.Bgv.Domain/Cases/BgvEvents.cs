using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Domain.Cases;

public sealed record CaseInitiatedDomainEvent(BgvCase Case) : IDomainEvent;

public sealed record CheckUpdatedDomainEvent(BgvCase Case, string CheckType, CheckStatus From, CheckStatus To, CaseActor Actor, DateTimeOffset At) : IDomainEvent;

public sealed record AdverseFlaggedDomainEvent(BgvCase Case, string CheckType, AdverseAction Action) : IDomainEvent;

public sealed record AdverseResolvedDomainEvent(BgvCase Case, AdverseOutcome Outcome, CaseActor Actor) : IDomainEvent;

/// <summary>Raised once per case; <paramref name="OnTime"/> feeds the cleared-on-time KPI (RCU-BGV-005).</summary>
public sealed record CaseClearedDomainEvent(BgvCase Case, bool OnTime) : IDomainEvent;
