using Recuro.BuildingBlocks.Domain;

namespace Recuro.Notification.Domain.Directory;

/// <summary>
/// This service's copy of the people it can notify: id, display name, address and roles, kept up to
/// date from Identity's events. Used to turn "everyone in HR Head" into email addresses.
/// </summary>
public sealed class DirectoryUser : Entity, ITenantOwned
{
    private string[] _roles = [];

    private DirectoryUser()
    {
    }

    public Guid TenantId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public string? Name { get; private set; }

    public string? Email { get; private set; }

    public IReadOnlyList<string> Roles => _roles;

    public DateTimeOffset UpdatedAt { get; private set; }

    public static DirectoryUser Create(string userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new DirectoryUser { Id = Guid.CreateVersion7(), UserId = userId, UpdatedAt = now };
    }

    /// <summary>Applies what an event says; fields it leaves out keep their value.</summary>
    public void Update(string? name, string? email, IReadOnlyList<string>? roles, DateTimeOffset now)
    {
        Name = string.IsNullOrWhiteSpace(name) ? Name : name;
        Email = string.IsNullOrWhiteSpace(email) ? Email : email.Trim();
        _roles = roles is null ? _roles : [.. roles];
        UpdatedAt = now;
    }
}

/// <summary>
/// Who started a flow, e.g. the HR-TA who submitted an MRF, so later events about the same subject
/// (approved, rejected) can reach them (FRD §5.6 #2 "Initiator").
/// </summary>
public sealed class SubjectOwner : Entity, ITenantOwned
{
    private SubjectOwner()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>CloudEvents subject, e.g. <c>Requisition/REQ-2026-0156</c>.</summary>
    public string Subject { get; private set; } = string.Empty;

    public string UserId { get; private set; } = string.Empty;

    public string? Name { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public static SubjectOwner Record(string subject, string userId, string? name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new SubjectOwner { Id = Guid.CreateVersion7(), Subject = subject, UserId = userId, Name = name, RecordedAt = now };
    }
}
