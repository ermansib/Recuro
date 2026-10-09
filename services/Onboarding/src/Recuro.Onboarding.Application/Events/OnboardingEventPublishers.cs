using System.Globalization;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Application.Events;

// Payloads carry ids, never personal data (services/contracts/events/onboarding.*.schema.json). The
// catalog's common fields are onbId, joiningDate and the item or milestone (RCU-BKD-001 §5). Dates are
// YYYY-MM-DD strings.

/// <summary>A §13 document the joining-instructions email lists.</summary>
public sealed record JoiningDocument(string Type, string Label, bool Mandatory);

/// <summary><c>onboarding.joining_instructions_sent.v1</c>: Notification emails the candidate now (RCU-ONB-001).</summary>
public sealed record JoiningInstructionsSentPayload(
    string OnbId,
    string AppId,
    string ReqId,
    string CandidateId,
    string? OfferId,
    string JoiningDate,
    IReadOnlyList<JoiningDocument> Documents);

/// <summary><c>onboarding.day1.ready.v1</c>: Finance and Payroll are notified (RCU-ONB-002).</summary>
public sealed record Day1ReadyPayload(string OnbId, string AppId, string ReqId, string CandidateId, string JoiningDate, DateTimeOffset ReadyAt, string ReadyBy);

/// <summary>
/// <c>onboarding.milestone.due.v1</c>: a touchpoint or probation milestone reached its date. Notification
/// reminds <c>assigneeRoles</c> and, for probation milestones, the reporting manager (RCU-ONB-001/004).
/// </summary>
public sealed record MilestoneDuePayload(
    string OnbId,
    string AppId,
    string ReqId,
    string CandidateId,
    string MilestoneId,
    string Milestone,
    string Label,
    string Phase,
    int Cycle,
    string DueOn,
    string JoiningDate,
    IReadOnlyList<string> AssigneeRoles,
    string? ReportingManagerId);

/// <summary><c>onboarding.employee.confirmed.v1</c> (RCU-ONB-005).</summary>
public sealed record EmployeeConfirmedPayload(string OnbId, string AppId, string ReqId, string CandidateId, string JoiningDate, DateTimeOffset ConfirmedAt, int Cycle, string DecidedBy);

/// <summary><c>onboarding.probation.extended.v1</c>: the audited extension and the new end date (RCU-ONB-005).</summary>
public sealed record ProbationExtendedPayload(
    string OnbId,
    string AppId,
    string ReqId,
    string CandidateId,
    int Cycle,
    int ExtendedByMonths,
    string NewProbationEnd,
    string Reason,
    string DecidedBy);

/// <summary>Turns domain events into integration events through the outbox, in the same transaction as the change.</summary>
internal sealed class OnboardingEventPublishers(IIntegrationEventPublisher publisher)
    : IDomainEventHandler<PreBoardingStartedDomainEvent>,
      IDomainEventHandler<Day1ReadyDomainEvent>,
      IDomainEventHandler<MilestoneDueDomainEvent>,
      IDomainEventHandler<EmployeeConfirmedDomainEvent>,
      IDomainEventHandler<ProbationExtendedDomainEvent>
{
    /// <summary>Who is reminded of every milestone; the reporting manager is added by id when known.</summary>
    public static readonly IReadOnlyList<string> MilestoneAssignees = ["hrta"];

    public Task Handle(PreBoardingStartedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var c = domainEvent.Case;
        publisher.Publish(
            EventTypes.Onboarding.JoiningInstructionsSent,
            Subject(c),
            new JoiningInstructionsSentPayload(
                c.Id.ToString(),
                c.AppId,
                c.ReqId,
                c.CandidateId,
                c.OfferId,
                Date(c.JoiningDate),
                c.Documents.OrderBy(d => d.Position).Select(d => new JoiningDocument(d.Type, d.Label, d.Mandatory)).ToList()));
        return Task.CompletedTask;
    }

    public Task Handle(Day1ReadyDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var c = domainEvent.Case;
        publisher.Publish(
            EventTypes.Onboarding.Day1Ready,
            Subject(c),
            new Day1ReadyPayload(c.Id.ToString(), c.AppId, c.ReqId, c.CandidateId, Date(c.JoiningDate), domainEvent.At, domainEvent.Actor.Id ?? domainEvent.Actor.Name));
        return Task.CompletedTask;
    }

    public Task Handle(MilestoneDueDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var c = domainEvent.Case;
        var m = domainEvent.Milestone;
        publisher.Publish(
            EventTypes.Onboarding.MilestoneDue,
            Subject(c),
            new MilestoneDuePayload(
                c.Id.ToString(),
                c.AppId,
                c.ReqId,
                c.CandidateId,
                m.Id.ToString(),
                m.Kind,
                m.Label,
                m.Phase.ToString(),
                m.Cycle,
                Date(m.DueOn),
                Date(c.JoiningDate),
                MilestoneAssignees,
                m.Phase == MilestonePhase.Probation ? c.ReportingManagerId : null));
        return Task.CompletedTask;
    }

    public Task Handle(EmployeeConfirmedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var c = domainEvent.Case;
        var d = domainEvent.Decision;
        publisher.Publish(
            EventTypes.Onboarding.EmployeeConfirmed,
            Subject(c),
            new EmployeeConfirmedPayload(c.Id.ToString(), c.AppId, c.ReqId, c.CandidateId, Date(c.JoiningDate), d.DecidedAt, d.Cycle, d.DecidedById ?? d.DecidedBy));
        return Task.CompletedTask;
    }

    public Task Handle(ProbationExtendedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var c = domainEvent.Case;
        var d = domainEvent.Decision;
        publisher.Publish(
            EventTypes.Onboarding.ProbationExtended,
            Subject(c),
            new ProbationExtendedPayload(
                c.Id.ToString(),
                c.AppId,
                c.ReqId,
                c.CandidateId,
                c.ProbationCycle,
                d.ExtendedByMonths ?? 0,
                Date(d.NewProbationEnd ?? c.ProbationEndsOn),
                d.Reason,
                d.DecidedById ?? d.DecidedBy));
        return Task.CompletedTask;
    }

    private static string Subject(OnboardingCase onboardingCase) => $"OnboardingCase/{onboardingCase.Id}";

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
