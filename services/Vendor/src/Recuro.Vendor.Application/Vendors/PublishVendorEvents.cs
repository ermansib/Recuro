using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Vendor.Domain.Vendors;

namespace Recuro.Vendor.Application.Vendors;

/// <summary><c>vendor.empanelled.v1</c> (RCU-BKD-001 §5: vendorId, gates).</summary>
public sealed record VendorEmpanelledPayload(string VendorId, string Type, int Gates, string ApprovedBy, DateTimeOffset EmpanelledAt);

/// <summary><c>vendor.de_empanelled.v1</c>; Bgv queues the vendor's open cases (reassignmentHint) and Notification tells the vendor.</summary>
public sealed record VendorDeEmpanelledPayload(string VendorId, string Type, string Reason, string ReassignmentHint, DateTimeOffset At);

internal sealed class PublishVendorEvents(IIntegrationEventPublisher publisher)
    : IDomainEventHandler<VendorEmpanelledDomainEvent>, IDomainEventHandler<VendorDeEmpanelledDomainEvent>
{
    public const string ReassignOpenCases = "reassign-open-cases";

    public Task Handle(VendorEmpanelledDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var v = domainEvent.Vendor;
        publisher.Publish(
            EventTypes.Vendor.Empanelled,
            $"Vendor/{v.Id}",
            new VendorEmpanelledPayload(v.Id.ToString(), v.Type.ToString(), VendorErrors.GateCount, domainEvent.Approver.Id ?? domainEvent.Approver.Name, v.EmpanelledAt!.Value));
        return Task.CompletedTask;
    }

    public Task Handle(VendorDeEmpanelledDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var v = domainEvent.Vendor;
        publisher.Publish(
            EventTypes.Vendor.DeEmpanelled,
            $"Vendor/{v.Id}",
            new VendorDeEmpanelledPayload(v.Id.ToString(), v.Type.ToString(), v.DeEmpanelReason!, ReassignOpenCases, v.DeEmpanelledAt!.Value));
        return Task.CompletedTask;
    }
}
