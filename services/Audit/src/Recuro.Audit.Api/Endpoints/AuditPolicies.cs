using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Audit.Api.Endpoints;

internal static class AuditPolicies
{
    /// <summary>HR staff read their tenant's trail (FRD §3.2).</summary>
    public const string Read = "audit.read";

    /// <summary>Only service accounts write directly; people's actions arrive through their services.</summary>
    public const string Ingest = "audit.ingest";

    public static IServiceCollection AddAuditPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Read, RecuroRoles.HrStaff)
            .AddRolePolicy(Ingest, RecuroRoles.Service);
        return services;
    }
}
