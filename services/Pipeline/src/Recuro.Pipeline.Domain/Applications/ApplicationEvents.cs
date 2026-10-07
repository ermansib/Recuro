using Recuro.BuildingBlocks.Domain;

namespace Recuro.Pipeline.Domain.Applications;

/// <summary>Who moved an application: a person, or a service reacting to an event.</summary>
public sealed record StageActor(string? Id, string Name, string? Role);

public sealed record ApplicationCreatedDomainEvent(Application Application) : IDomainEvent;

/// <summary>RCU-PPL-007: every transition, with how long the application sat in the previous stage.</summary>
public sealed record StageChangedDomainEvent(
    Application Application,
    ApplicationStage From,
    ApplicationStage To,
    StageActor Actor,
    DateTimeOffset At,
    TimeSpan Dwell) : IDomainEvent;

public sealed record FinalRejectedDomainEvent(Application Application, string Reason, DateOnly RegretDueBy, DateOnly RetainUntil) : IDomainEvent;

public sealed record TatBreachedDomainEvent(Application Application, ApplicationStage Stage, DateTimeOffset DueAt, TimeSpan Variance) : IDomainEvent;
