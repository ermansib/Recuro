using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Identity.Api.Endpoints;

internal static class IdentityPolicies
{
    /// <summary>HR staff list their team (frontend listTeam); services look up recipients by role.</summary>
    public const string ReadUsers = "identity.users.read";

    public static IServiceCollection AddIdentityPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(ReadUsers, [.. RecuroRoles.HrStaff, RecuroRoles.Service]);
        return services;
    }
}
