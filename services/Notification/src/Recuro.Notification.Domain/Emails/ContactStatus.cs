using Recuro.BuildingBlocks.Domain;

namespace Recuro.Notification.Domain.Emails;

/// <summary>
/// Deliverability of one email address in a tenant (RCU-NTF-002 bounce handling). A hard bounce stops
/// further mail to the address until it is cleared.
/// </summary>
public sealed class ContactStatus : Entity, ITenantOwned
{
    private ContactStatus()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Lower-cased address.</summary>
    public string Address { get; private set; } = string.Empty;

    public bool Bounced { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ContactStatus ForAddress(string address, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return new ContactStatus { Id = Guid.CreateVersion7(), Address = Normalise(address), UpdatedAt = now };
    }

    public static string Normalise(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.Trim().ToLowerInvariant();
    }

    public void RecordBounce(string reason, DateTimeOffset now)
    {
        Bounced = true;
        Reason = reason;
        UpdatedAt = now;
    }
}
