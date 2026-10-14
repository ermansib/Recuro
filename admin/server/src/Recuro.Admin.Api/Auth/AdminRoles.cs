namespace Recuro.Admin.Api.Auth;

/// <summary>Role and policy names. Roles come from the identity provider's token (or the development sign-in).</summary>
public static class AdminRoles
{
    public const string PlatformAdmin = "platform-admin";
    public const string TenantAdmin = "tenant-admin";

    /// <summary>Recuro microservices' service accounts (client credentials, RCU-AUT-005).</summary>
    public const string Service = "service";
}

public static class AdminPolicies
{
    public const string Platform = "platform";
    public const string Tenant = "tenant";
    public const string Service = "service";

    /// <summary>Anyone signed in to the main portal of a workspace (portal token with a workspace id).</summary>
    public const string WorkspacePerson = "workspace-person";
}

public static class AdminClaims
{
    public const string Tenant = "tenant_id";

    public const string Subject = "sub";

    /// <summary>Workspace role keys the realm maps from the person's <c>workspace_roles</c> attribute.</summary>
    public const string WorkspaceRoles = "workspace_roles";
}
