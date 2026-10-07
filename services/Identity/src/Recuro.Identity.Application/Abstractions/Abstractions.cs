using Recuro.Identity.Domain.Access;
using Recuro.Identity.Domain.Masking;
using Recuro.Identity.Domain.Users;

namespace Recuro.Identity.Application.Abstractions;

/// <summary>The current tenant's user mirror. Never returns another tenant's rows.</summary>
public interface IUserAccounts
{
    Task<UserAccount?> FindBySubjectAsync(string subject, CancellationToken ct);

    /// <summary>People in the tenant, optionally only those holding <paramref name="role"/>, by name.</summary>
    Task<IReadOnlyList<UserAccount>> ListAsync(string? role, CancellationToken ct);

    /// <summary>
    /// Inserts a newly provisioned user and commits. Returns false when a concurrent request already
    /// provisioned the same person; the caller then reads that row instead.
    /// </summary>
    Task<bool> TryAddAsync(UserAccount user, CancellationToken ct);
}

/// <summary>The RBAC policy in force for the current tenant (RCU-AUT-003).</summary>
public interface IAccessPolicyProvider
{
    Task<AccessPolicy> GetAsync(CancellationToken ct);
}

/// <summary>The masking map in force for the current tenant (RCU-AUT-004).</summary>
public interface IMaskingMapProvider
{
    Task<MaskingMap> GetAsync(CancellationToken ct);
}
