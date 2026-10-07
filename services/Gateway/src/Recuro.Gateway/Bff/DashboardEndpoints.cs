using System.Net.Http.Headers;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;

namespace Recuro.Gateway.Bff;

/// <summary>Backend-for-frontend endpoints served by the gateway itself (RCU-GTW-002).</summary>
internal static class DashboardEndpoints
{
    public const string Path = "/bff/dashboard/ta";
    public const string Policy = "bff.dashboard";

    /// <summary>Names the sources that could not answer, so the client can tell "—" from zero.</summary>
    public const string DegradedHeader = "X-Recuro-Degraded";

    public static IServiceCollection AddDashboardBff(this IServiceCollection services)
    {
        services.AddOptions<DashboardOptions>().BindConfiguration(DashboardOptions.SectionName);
        services.AddHttpClient(DashboardAggregator.ClientName).AddHttpMessageHandler<CorrelationHeadersHandler>();
        services.AddScoped<DashboardAggregator>();
        services.AddAuthorizationBuilder().AddRolePolicy(Policy, RecuroRoles.HrStaff);
        return services;
    }

    public static IEndpointRouteBuilder MapDashboardBff(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, GetAsync)
            .RequireAuthorization(Policy)
            .RequireRateLimiting(GatewayRateLimits.PerUser)
            .WithTags("BFF")
            .WithSummary("All dashboard tiles in one call (frontend getDashboard); unavailable sources show \"—\".");
        return app;
    }

    private static async Task<IResult> GetAsync(HttpContext http, DashboardAggregator aggregator, CancellationToken ct)
    {
        var caller = AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization.ToString(), out var header) ? header : null;
        var result = await aggregator.GetAsync(caller, ct);
        if (result.Unavailable.Count > 0)
        {
            http.Response.Headers[DegradedHeader] = string.Join(',', result.Unavailable);
        }

        return Results.Ok(result.Data);
    }
}
