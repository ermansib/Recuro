using Recuro.BuildingBlocks.Domain;

namespace Recuro.Onboarding.Domain.Cases;

/// <summary>RCU-ONB-001: the case opened on acceptance; the joining-instructions email goes out now.</summary>
public sealed record PreBoardingStartedDomainEvent(OnboardingCase Case, DateTimeOffset At) : IDomainEvent;

/// <summary>RCU-ONB-002: every Day-1 item is done.</summary>
public sealed record Day1ReadyDomainEvent(OnboardingCase Case, CaseActor Actor, DateTimeOffset At) : IDomainEvent;

/// <summary>RCU-ONB-001/004: a touchpoint or probation milestone reached its date.</summary>
public sealed record MilestoneDueDomainEvent(OnboardingCase Case, Milestone Milestone) : IDomainEvent;

/// <summary>RCU-ONB-005: the hire was confirmed at the end of probation.</summary>
public sealed record EmployeeConfirmedDomainEvent(OnboardingCase Case, ProbationDecision Decision) : IDomainEvent;

/// <summary>RCU-ONB-005: probation was extended; a new milestone cycle starts.</summary>
public sealed record ProbationExtendedDomainEvent(OnboardingCase Case, ProbationDecision Decision) : IDomainEvent;
