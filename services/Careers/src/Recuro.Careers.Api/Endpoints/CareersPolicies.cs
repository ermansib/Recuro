using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Careers.Api.Endpoints;

/// <summary>FRD §3.2 for the careers site. The public endpoints are anonymous and rate-limited at the gateway.</summary>
internal static class CareersPolicies
{
    /// <summary>HR staff see every posting with its status and log.</summary>
    public const string ReadPostings = "careers.postings.read";

    /// <summary>RCU-CAR-007: HR-TA writes and publishes postings; the HR Head may also publish (and open early) or take one down.</summary>
    public const string ManagePostings = "careers.postings.manage";

    public static IServiceCollection AddCareersPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(ReadPostings, RecuroRoles.HrStaff)
            .AddRolePolicy(ManagePostings, RecuroRoles.HrTa, RecuroRoles.HrHead);
        return services;
    }
}
