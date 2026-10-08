using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Vendor.Api.Endpoints;

/// <summary>FRD §3.2: HR Head empanels and de-empanels (<c>vendor.empanel</c>); HR staff and services read.</summary>
internal static class VendorPolicies
{
    /// <summary>The S-14 list: HR staff (MD/CEO and HR-TA read-only) and services.</summary>
    public const string Read = "vendor.read";

    /// <summary>
    /// The active check (architecture.md): any signed-in caller, since Candidate forwards whoever logs a
    /// consultant-sourced candidate and Bgv whoever initiates a case.
    /// </summary>
    public const string Status = "vendor.status";

    /// <summary>RCU-VEN-001/004: HR Head only.</summary>
    public const string Manage = "vendor.manage";

    public static IServiceCollection AddVendorPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Read, RecuroRoles.HrTa, RecuroRoles.HrHead, RecuroRoles.MdCeo, RecuroRoles.Service)
            .AddPolicy(Status, policy => policy.RequireAuthenticatedUser())
            .AddRolePolicy(Manage, RecuroRoles.HrHead);
        return services;
    }
}
