namespace Recuro.Admin.Api.Auth;

/// <summary>Role and policy names. Roles come from the identity provider's token (or the development sign-in).</summary>
public static class AdminRoles
{
    public const string PlatformAdmin = "platform-admin";
    public const string TenantAdmin = "tenant-admin";
}

public static class AdminPolicies
{
    public const string Platform = "platform";
    public const string Tenant = "tenant";
}

public static class AdminClaims
{
    public const string Tenant = "tenant_id";
}
