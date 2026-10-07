using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Gateway;

/// <summary>Bound from <c>Gateway:RateLimits</c>.</summary>
internal sealed class GatewayRateLimitOptions
{
    public const string SectionName = "Gateway:RateLimits";

    /// <summary>Requests per user (or per IP when anonymous) per window on authenticated routes.</summary>
    public int PerUserPermits { get; set; } = 600;

    /// <summary>Requests per IP per window on public routes (careers site).</summary>
    public int PublicPermits { get; set; } = 120;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// RCU-GTW-004: per-route quotas. Over quota the caller gets 429 with Retry-After. Route policies are
/// named in the YARP route config (<c>RateLimiterPolicy</c>).
/// </summary>
internal static class GatewayRateLimits
{
    public const string PerUser = "per-user";
    public const string Public = "public";

    public static IServiceCollection AddGatewayRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(GatewayRateLimitOptions.SectionName).Get<GatewayRateLimitOptions>() ?? new GatewayRateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            limiter.AddPolicy(PerUser, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirst(RecuroClaims.Subject)?.Value ?? ClientAddress(http),
                _ => Window(options.PerUserPermits, options.Window)));

            limiter.AddPolicy(Public, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientAddress(http),
                _ => Window(options.PublicPermits, options.Window)));
        });

        return services;
    }

    private static string ClientAddress(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) => new()
    {
        PermitLimit = permits,
        Window = window,
        QueueLimit = 0,
    };
}
