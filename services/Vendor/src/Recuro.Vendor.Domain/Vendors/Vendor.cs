using Recuro.BuildingBlocks.Domain;

namespace Recuro.Vendor.Domain.Vendors;

/// <summary>Who acted on a vendor record.</summary>
public sealed record VendorActor(string? Id, string Name);

/// <summary>
/// An empanelled partner: a recruitment consultant or a BGV agency (FRD §15, S-14). It becomes Active only
/// when all six §15 gates are met, the NDA, agreement and privacy acknowledgement are on file, and HR Head
/// approves; de-empanelment switches it Off and tells BGV to reassign its open cases.
/// </summary>
public sealed class Vendor : AggregateRoot, ITenantOwned
{
    private Vendor()
    {
    }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public VendorType Type { get; private set; }

    public FeeTerms Fee { get; private set; } = null!;

    public EmpanelmentGates Gates { get; private set; } = EmpanelmentGates.None;

    public VendorDocuments Documents { get; private set; } = VendorDocuments.None;

    public VendorStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset? EmpanelledAt { get; private set; }

    /// <summary>The HR Head who approved empanelment (§15 "HR Head approval logged").</summary>
    public string? ApprovedBy { get; private set; }

    public DateTimeOffset? DeEmpanelledAt { get; private set; }

    public string? DeEmpanelReason { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Vendor Register(string name, VendorType type, FeeTerms fee, EmpanelmentGates gates, VendorDocuments documents, VendorActor actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(fee);
        ArgumentNullException.ThrowIfNull(gates);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(actor);
        return new Vendor
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Type = type,
            Fee = fee,
            Gates = gates,
            Documents = documents,
            Status = VendorStatus.Pending,
            CreatedAt = now,
            CreatedBy = actor.Name,
            UpdatedAt = now,
        };
    }

    public bool IsActive => Status == VendorStatus.Active;

    /// <summary>Records gate evidence and documents while the vendor is still pending.</summary>
    public Result UpdateEmpanelment(EmpanelmentGates gates, VendorDocuments documents, FeeTerms fee, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(gates);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(fee);
        if (Status != VendorStatus.Pending)
        {
            return VendorErrors.NotPending(Status);
        }

        (Gates, Documents, Fee, UpdatedAt) = (gates, documents, fee, now);
        return Result.Success();
    }

    /// <summary>
    /// RCU-VEN-001 / VND-001: Active only with all six gates, the three documents and HR Head approval.
    /// Otherwise 400 with the pending count, like the prototype's "n gates pending" message.
    /// </summary>
    public Result Empanel(FeeBand band, VendorActor approver, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(approver);
        var allowed = VendorTransitions.Table.EnsureCanMove(Status, VendorStatus.Active, "Vendor");
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var pending = Gates.Pending();
        var missing = Documents.Missing();
        if (pending.Count > 0 || missing.Count > 0)
        {
            return VendorErrors.GatesPending(pending, missing);
        }

        if (!band.Contains(Fee) && string.IsNullOrWhiteSpace(Fee.Justification))
        {
            return VendorErrors.JustificationRequired(band);
        }

        Status = VendorStatus.Active;
        EmpanelledAt = now;
        ApprovedBy = approver.Name;
        UpdatedAt = now;
        Raise(new VendorEmpanelledDomainEvent(this, approver));
        return Result.Success();
    }

    /// <summary>RCU-VEN-004 / VND-003: switches the vendor Off with a documented reason.</summary>
    public Result DeEmpanel(string reason, VendorActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return VendorErrors.ReasonRequired;
        }

        var allowed = VendorTransitions.Table.EnsureCanMove(Status, VendorStatus.Off, "Vendor");
        if (allowed.IsFailure)
        {
            return allowed;
        }

        Status = VendorStatus.Off;
        DeEmpanelledAt = now;
        DeEmpanelReason = reason.Trim();
        UpdatedAt = now;
        Raise(new VendorDeEmpanelledDomainEvent(this, actor));
        return Result.Success();
    }
}

public sealed record VendorEmpanelledDomainEvent(Vendor Vendor, VendorActor Approver) : IDomainEvent;

public sealed record VendorDeEmpanelledDomainEvent(Vendor Vendor, VendorActor Actor) : IDomainEvent;
