using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Application.Workspaces;

/// <summary>
/// A workspace's sign-in and careers settings. Same fields and casing as the frontend's <c>TenantConfig</c>
/// (frontend/src/domain/types.ts), minus careers copy that the workspace fills in later.
/// </summary>
public sealed record WorkspaceDto(
    Guid Id,
    string Slug,
    string Name,
    OrgType OrgType,
    string CareersTagline,
    string? EmailDomain,
    string Locale,
    string Currency,
    IReadOnlyList<string> SsoProviders,
    IReadOnlyList<string> MfaRoles,
    int SessionIdleMinutes)
{
    public static WorkspaceDto From(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return new(
            tenant.Id,
            tenant.Slug,
            tenant.Name,
            tenant.OrgType,
            tenant.CareersTagline,
            tenant.EmailDomain,
            tenant.Locale,
            tenant.Currency,
            tenant.SsoProviders,
            tenant.MfaRoles,
            tenant.SessionIdleMinutes);
    }
}

/// <summary>The workspace owner's account. <see cref="Id"/> is the Keycloak subject.</summary>
public sealed record WorkspaceOwnerDto(string Id, string Name, string Email, string Role);

public sealed record WorkspaceSignUpDto(WorkspaceDto Workspace, WorkspaceOwnerDto Owner);
