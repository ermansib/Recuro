namespace Recuro.BuildingBlocks.Application.Abstractions;

/// <summary>
/// The tenant of the current request or message. Set once per scope (HTTP middleware or the event
/// consumer); persistence reads it to filter and stamp rows.
/// </summary>
public interface ITenantContext
{
    /// <summary>Null when the scope has no tenant, for example an anonymous request.</summary>
    Guid? TenantId { get; }

    /// <summary>The tenant, or an exception when none is set. Use in code paths that require one.</summary>
    Guid RequiredTenantId => TenantId ?? throw new InvalidOperationException("No tenant is set for this scope.");
}

/// <summary>Who is acting in the current scope: a person from the token, or a service account.</summary>
public interface ICurrentUser
{
    /// <summary>Stable subject id from the identity provider (Keycloak <c>sub</c>).</summary>
    string? UserId { get; }

    /// <summary>Display name for audit and notifications.</summary>
    string? Name { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role) => Roles.Contains(role, StringComparer.Ordinal);
}

/// <summary>Commits the current unit of work: entity changes, outbox and inbox rows in one transaction.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Correlation ids for the current request or message (X-Request-ID, X-Correlation-ID).</summary>
public interface ICorrelationContext
{
    string? RequestId { get; }

    string? CorrelationId { get; }
}

/// <summary>Tag names for health checks: <c>/ready</c> runs the ones tagged <see cref="Ready"/>.</summary>
public static class HealthTags
{
    public const string Ready = "ready";
}
