using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Config.Api.Endpoints;

internal static class ConfigPolicies
{
    /// <summary>Tenant administrator in the admin portal (Keycloak realm role).</summary>
    public const string TenantAdminRole = "tenant-admin";

    /// <summary>HR staff, tenant admins and services read rules and resolve them.</summary>
    public const string Read = "config.read";

    /// <summary>HR Ops proposes changes (FRD §3.2: config is managed with an approval workflow).</summary>
    public const string Propose = "config.propose";

    /// <summary>A second, senior person approves or rejects them (RCU-CFG-001 dual approval).</summary>
    public const string Decide = "config.decide";

    public static IServiceCollection AddConfigPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Read, [.. RecuroRoles.HrStaff, TenantAdminRole, RecuroRoles.Service])
            .AddRolePolicy(Propose, RecuroRoles.HrTa, RecuroRoles.HrHead, TenantAdminRole)
            .AddRolePolicy(Decide, RecuroRoles.HrHead, RecuroRoles.MdCeo, TenantAdminRole);
        return services;
    }
}
