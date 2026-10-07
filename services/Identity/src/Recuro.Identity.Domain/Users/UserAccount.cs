using Recuro.BuildingBlocks.Domain;

namespace Recuro.Identity.Domain.Users;

/// <summary>
/// The service's mirror of a Keycloak account (RCU-AUT-001). Keycloak stays the source of truth for
/// sign-in, MFA and lockout; the mirror is created just in time from the first token and refreshed
/// whenever the person's name, email or roles change, so other services can list people by role.
/// </summary>
public sealed class UserAccount : AggregateRoot, ITenantOwned
{
    private List<string> _roles = [];

    private UserAccount()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Keycloak <c>sub</c>. The id every other service records for this person.</summary>
    public string Subject { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    /// <summary>Mirrored roles, sorted, without Keycloak's built-in defaults.</summary>
    public IReadOnlyList<string> Roles => _roles;

    public DateTimeOffset FirstSeenAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>JIT provisioning from the first token the service sees for this person.</summary>
    public static UserAccount Provision(Guid tenantId, string subject, string name, string email, IEnumerable<string> roles, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A user belongs to a tenant.", nameof(tenantId));
        }

        var user = new UserAccount
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Subject = subject,
            Name = string.IsNullOrWhiteSpace(name) ? subject : name.Trim(),
            Email = email?.Trim() ?? string.Empty,
            _roles = Normalise(roles),
            FirstSeenAt = now,
            LastSeenAt = now,
        };
        user.Raise(new UserProvisionedDomainEvent(user.Subject, user.Roles));
        return user;
    }

    /// <summary>Refreshes the mirror from a newer token. Raises <see cref="UserRolesChangedDomainEvent"/> when the roles differ.</summary>
    public void SyncFromToken(string name, string email, IEnumerable<string> roles, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            Email = email.Trim();
        }

        var newRoles = Normalise(roles);
        if (!newRoles.SequenceEqual(_roles, StringComparer.Ordinal))
        {
            var previous = _roles;
            _roles = newRoles;
            Raise(new UserRolesChangedDomainEvent(Subject, previous, newRoles));
        }

        LastSeenAt = now;
    }

    private static List<string> Normalise(IEnumerable<string> roles) =>
        (roles ?? []).Where(PersonaRoles.IsMirrored).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
}

/// <summary>A person signed in for the first time.</summary>
public sealed record UserProvisionedDomainEvent(string Subject, IReadOnlyList<string> Roles) : IDomainEvent;

/// <summary>A person's roles changed in the identity provider.</summary>
public sealed record UserRolesChangedDomainEvent(string Subject, IReadOnlyList<string> From, IReadOnlyList<string> To) : IDomainEvent;
