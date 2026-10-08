using Recuro.BuildingBlocks.Domain;
using Recuro.Reporting.Domain.Periods;

namespace Recuro.Reporting.Domain.Reports;

/// <summary>
/// Recruitment spend for one month and source channel, for Cost-per-Hire (FRD §12, RCU-RPT-003). No
/// event carries spend, so HR Head records it (or imports it) here. Consultant lines are the fees billed
/// under the vendor agreement, so the channel's CPH is the true cost.
/// </summary>
public sealed class RecruitmentCost : Entity, ITenantOwned
{
    private RecruitmentCost()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>First day of the month the spend belongs to.</summary>
    public DateOnly Month { get; private set; }

    public string Source { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    /// <summary>ISO 4217 code.</summary>
    public string Currency { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    public string RecordedBy { get; private set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; private set; }

    public static RecruitmentCost Record(DateOnly month, string source, decimal amount, string currency, string? note, string recordedBy, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        Month = new DateOnly(month.Year, month.Month, 1),
        Source = source,
        Amount = amount,
        Currency = currency.ToUpperInvariant(),
        Note = note,
        RecordedBy = recordedBy,
        RecordedAt = at,
    };
}

/// <summary>
/// A tenant's reporting preferences: the time zone periods and schedules use (RCU-RPT-003 "tenant-tz"),
/// and which scheduled packs are sent to which role.
/// </summary>
public sealed class ReportSettings : Entity, ITenantOwned
{
    private ReportSettings()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>IANA id, e.g. <c>Asia/Kolkata</c>.</summary>
    public string TimeZone { get; private set; } = "UTC";

    public bool MonthlyPack { get; private set; } = true;

    public string MonthlyRecipientRole { get; private set; } = "hrhead";

    public bool QuarterlyPack { get; private set; } = true;

    public string QuarterlyRecipientRole { get; private set; } = "mdceo";

    public DateTimeOffset UpdatedAt { get; private set; }

    public string? UpdatedBy { get; private set; }

    public static ReportSettings Create(string timeZone, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        TimeZone = timeZone,
        UpdatedAt = at,
    };

    public void Update(string timeZone, bool monthlyPack, string monthlyRole, bool quarterlyPack, string quarterlyRole, string updatedBy, DateTimeOffset at)
    {
        TimeZone = timeZone;
        MonthlyPack = monthlyPack;
        MonthlyRecipientRole = monthlyRole;
        QuarterlyPack = quarterlyPack;
        QuarterlyRecipientRole = quarterlyRole;
        UpdatedBy = updatedBy;
        UpdatedAt = at;
    }

    public bool PackEnabled(Cadence cadence) => cadence == Cadence.Monthly ? MonthlyPack : QuarterlyPack;

    public string RecipientRole(Cadence cadence) => cadence == Cadence.Monthly ? MonthlyRecipientRole : QuarterlyRecipientRole;
}

/// <summary>
/// The KPI register of one period, frozen (RCU-RPT-002). A recompute (RCU-RPT-005) adds a new revision
/// and never changes an old one; <see cref="Hash"/> (SHA-256 of <see cref="Payload"/>) makes any later
/// change to a stored register detectable.
/// </summary>
public sealed class KpiSnapshot : Entity, ITenantOwned
{
    private KpiSnapshot()
    {
    }

    public Guid TenantId { get; private set; }

    public string Period { get; private set; } = string.Empty;

    public Cadence Cadence { get; private set; }

    public int Revision { get; private set; }

    public int DefinitionsVersion { get; private set; }

    /// <summary>Log offset the register was computed up to.</summary>
    public long UpToSequence { get; private set; }

    public string Payload { get; private set; } = "{}";

    public string Hash { get; private set; } = string.Empty;

    public string? Reason { get; private set; }

    public string ComputedBy { get; private set; } = string.Empty;

    public DateTimeOffset ComputedAt { get; private set; }

    public static KpiSnapshot Freeze(ReportPeriod period, int revision, int definitionsVersion, long upToSequence, string payload, string hash, string? reason, string computedBy, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(period);
        return new KpiSnapshot
        {
            Id = Guid.CreateVersion7(),
            Period = period.Key,
            Cadence = period.Cadence,
            Revision = revision,
            DefinitionsVersion = definitionsVersion,
            UpToSequence = upToSequence,
            Payload = payload,
            Hash = hash,
            Reason = reason,
            ComputedBy = computedBy,
            ComputedAt = at,
        };
    }
}

/// <summary>Where a pack's email stands, as Notification reports it back.</summary>
public enum PackDelivery
{
    /// <summary>Built and archived only; nobody was emailed.</summary>
    Archived,

    /// <summary>Handed to Notification; waiting for its dispatch report.</summary>
    Queued,
    Sent,
    Failed,
}

/// <summary>An archived report pack (RCU-RPT-003): the PDF and CSV for one period and recipient role.</summary>
public sealed class ReportPack : Entity, ITenantOwned
{
    private ReportPack()
    {
    }

    public Guid TenantId { get; private set; }

    public string Period { get; private set; } = string.Empty;

    public Cadence Cadence { get; private set; }

    public string RecipientRole { get; private set; } = string.Empty;

    public Guid SnapshotId { get; private set; }

    public string SnapshotHash { get; private set; } = string.Empty;

    public byte[] Pdf { get; private set; } = [];

    public string Csv { get; private set; } = string.Empty;

    public PackDelivery Delivery { get; private set; }

    /// <summary>Id of the <c>reporting.pack.ready</c> event; Notification's dispatch report refers to it.</summary>
    public Guid? ReadyEventId { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static ReportPack Archive(KpiSnapshot snapshot, string recipientRole, byte[] pdf, string csv, string createdBy, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ReportPack
        {
            Id = Guid.CreateVersion7(),
            Period = snapshot.Period,
            Cadence = snapshot.Cadence,
            RecipientRole = recipientRole,
            SnapshotId = snapshot.Id,
            SnapshotHash = snapshot.Hash,
            Pdf = pdf,
            Csv = csv,
            Delivery = PackDelivery.Archived,
            CreatedBy = createdBy,
            CreatedAt = at,
        };
    }

    public void Queued(Guid readyEventId)
    {
        ReadyEventId = readyEventId;
        Delivery = PackDelivery.Queued;
    }

    /// <summary>
    /// Notification reports once per recipient. Sent wins: the pack is Sent once any recipient got it, and
    /// stays Failed only while every outcome so far has failed.
    /// </summary>
    public void Delivered(bool sent, DateTimeOffset at)
    {
        if (Delivery == PackDelivery.Sent)
        {
            return;
        }

        Delivery = sent ? PackDelivery.Sent : PackDelivery.Failed;
        DeliveredAt = at;
    }
}
