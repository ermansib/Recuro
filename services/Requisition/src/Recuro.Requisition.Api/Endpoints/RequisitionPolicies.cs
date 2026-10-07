using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Requisition.Api.Endpoints;

/// <summary>RBAC from FRD §3.2 (frontend auth/permissions.ts), enforced here, not just hidden in the UI.</summary>
internal static class RequisitionPolicies
{
    /// <summary>HR staff read the tracker; services read it for the sourcing gate (RCU-REQ-005).</summary>
    public const string Read = "requisitions.read";

    /// <summary><c>mrf.raise</c>: only HR-TA raises and edits MRFs.</summary>
    public const string Raise = "requisitions.raise";

    /// <summary>RCU-REQ-008: HR Head cancels.</summary>
    public const string Cancel = "requisitions.cancel";

    /// <summary>HR staff read job descriptions.</summary>
    public const string ReadJd = "jd.read";

    /// <summary><c>jd.edit</c>: HR-TA owns JD drafts.</summary>
    public const string EditJd = "jd.edit";

    public static IServiceCollection AddRequisitionPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Read, [.. RecuroRoles.HrStaff, RecuroRoles.Service])
            .AddRolePolicy(Raise, RecuroRoles.HrTa)
            .AddRolePolicy(Cancel, RecuroRoles.HrHead)
            .AddRolePolicy(ReadJd, RecuroRoles.HrStaff)
            .AddRolePolicy(EditJd, RecuroRoles.HrTa);
        return services;
    }
}
