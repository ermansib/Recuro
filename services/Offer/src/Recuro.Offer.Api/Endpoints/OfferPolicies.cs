using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Offer.Api.Endpoints;

/// <summary>RBAC from FRD §3.2 (frontend auth/permissions.ts), enforced here, not just hidden in the UI.</summary>
internal static class OfferPolicies
{
    /// <summary>HR staff read offers (masked per role); services read them too.</summary>
    public const string Read = "offers.read";

    /// <summary><c>offer.edit</c>: HR-TA drafts, revises, submits and records the candidate's answer.</summary>
    public const string Edit = "offers.edit";

    /// <summary><c>offer.approve</c>: HR Head or MD/CEO, whoever the route names (Workflow checks the assignee).</summary>
    public const string Approve = "offers.approve";

    /// <summary><c>offer.release</c>: HR-TA or HR Head send the letter.</summary>
    public const string Release = "offers.release";

    /// <summary>RCU-OFR-006: HR Head, or MD/CEO per §16, withdraw with a reason.</summary>
    public const string Withdraw = "offers.withdraw";

    public static IServiceCollection AddOfferPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Read, [.. RecuroRoles.HrStaff, RecuroRoles.Service])
            .AddRolePolicy(Edit, RecuroRoles.HrTa)
            .AddRolePolicy(Approve, [RecuroRoles.HrHead, RecuroRoles.MdCeo])
            .AddRolePolicy(Release, [RecuroRoles.HrTa, RecuroRoles.HrHead])
            .AddRolePolicy(Withdraw, [RecuroRoles.HrHead, RecuroRoles.MdCeo]);
        return services;
    }
}
