using System.Threading.RateLimiting;
using Recuro.Admin.Api.Endpoints;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Api.Http;

/// <summary>The "SignUp" configuration section.</summary>
public sealed class SignUpOptions
{
    public const string SectionName = "SignUp";

    public string DefaultLocale { get; set; } = WorkspaceDefaults.Locale;

    public string DefaultCurrency { get; set; } = WorkspaceDefaults.Currency;

    /// <summary>Sign-ups one caller (IP address) may make per <see cref="WindowMinutes"/>.</summary>
    public int PermitsPerWindow { get; set; } = 10;

    public int WindowMinutes { get; set; } = 15;
}

internal static class SignUpSetup
{
    public static IServiceCollection AddWorkspaceSignUp(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(SignUpOptions.SectionName).Get<SignUpOptions>() ?? new SignUpOptions();
        services.AddSingleton(new SignUpDefaults(options.DefaultLocale, options.DefaultCurrency));
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(WorkspaceEndpoints.SignUpRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitsPerWindow,
                    Window = TimeSpan.FromMinutes(options.WindowMinutes),
                    QueueLimit = 0,
                }));
        });
        return services;
    }
}
