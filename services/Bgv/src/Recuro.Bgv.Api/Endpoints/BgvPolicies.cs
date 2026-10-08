using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Bgv.Api.Endpoints;

/// <summary>FRD §3.2 for BGV, the same matrix as the frontend's <c>bgv.initiate</c>, <c>bgv.reportAdverse</c> and <c>offer.release</c>.</summary>
internal static class BgvPolicies
{
    /// <summary>HR staff see cases (MD/CEO without sensitive notes); Offer and Onboarding read the release gate.</summary>
    public const string Read = "bgv.read";

    /// <summary>RCU-BGV-001/003/004: HR-TA initiates, updates checks and reports adverse findings.</summary>
    public const string Operate = "bgv.operate";

    /// <summary>RCU-BGV-005: HR-TA and HR Head release the offer.</summary>
    public const string Release = "bgv.release";

    /// <summary>RCU-VND-003: HR-TA and HR Head move a de-empanelled vendor's cases.</summary>
    public const string Reassign = "bgv.reassign";

    public static IServiceCollection AddBgvPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Read, RecuroRoles.HrTa, RecuroRoles.HrHead, RecuroRoles.MdCeo, RecuroRoles.Service)
            .AddRolePolicy(Operate, RecuroRoles.HrTa)
            .AddRolePolicy(Release, RecuroRoles.HrTa, RecuroRoles.HrHead)
            .AddRolePolicy(Reassign, RecuroRoles.HrTa, RecuroRoles.HrHead);
        return services;
    }
}
