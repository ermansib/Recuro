using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;
using Serilog;
using Serilog.Formatting.Compact;

namespace Recuro.BuildingBlocks.Web;

/// <summary>
/// The golden service scaffold (RCU-PLT-001) as two calls: <see cref="AddRecuroServiceDefaults"/> on the
/// builder and <see cref="UseRecuroServiceDefaults"/> on the app. Every service and the gateway use them.
/// </summary>
public static class ServiceDefaults
{
    public const string LivenessPath = "/health";
    public const string ReadinessPath = "/ready";
    public const string MetricsPath = "/metrics";

    public static WebApplicationBuilder AddRecuroServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        // Structured JSON logs to stdout. Every line carries RequestId, CorrelationId, TenantId and the trace id.
        services.AddSerilog((sp, log) => log
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(sp)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(new RenderedCompactJsonFormatter()));

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !IsInfrastructurePath(ctx.Request.Path))
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql")
                .AddSource("Recuro.*"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddPrometheusExporter());
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        services.AddHealthChecks();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Extensions["requestId"] = ctx.HttpContext.Request.Headers[CorrelationMiddleware.RequestIdHeader].ToString();
            ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString();
        });
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddOpenApi();
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationHeadersHandler>();

        services.TryAddScoped<ScopeContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<ScopeContext>());
        services.TryAddScoped<ICurrentUser>(sp => sp.GetRequiredService<ScopeContext>());
        services.TryAddScoped<ICorrelationContext>(sp => sp.GetRequiredService<ScopeContext>());

        // Idempotency replays live in Redis when configured (shared by replicas), otherwise in memory.
        var redis = builder.Configuration.GetConnectionString("redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis;
                options.InstanceName = $"{serviceName}:";
            });
        }

        services.AddRecuroAuth(builder.Configuration, builder.Environment);
        services.AddRecuroServiceTokens(builder.Configuration, builder.Environment, serviceName);
        return builder;
    }

    /// <summary>
    /// Middleware order matters: correlation → errors → logging → auth → scope → idempotency.
    /// The gateway passes <paramref name="idempotency"/> false: replays are the owning service's job.
    /// </summary>
    public static WebApplication UseRecuroServiceDefaults(this WebApplication app, bool idempotency = true)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<CorrelationMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSerilogRequestLogging(options =>
            options.GetLevel = (ctx, _, ex) => ex is not null || ctx.Response.StatusCode >= 500
                ? Serilog.Events.LogEventLevel.Error
                : IsInfrastructurePath(ctx.Request.Path) ? Serilog.Events.LogEventLevel.Verbose : Serilog.Events.LogEventLevel.Information);
        app.UseAuthentication();
        app.UseMiddleware<ScopeContextMiddleware>();
        app.UseAuthorization();
        if (idempotency)
        {
            app.UseMiddleware<IdempotencyMiddleware>();
        }

        // Liveness: the process answers. Readiness: its dependencies (database, broker) answer.
        app.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks(ReadinessPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Ready) }).AllowAnonymous();
        app.MapPrometheusScrapingEndpoint(MetricsPath).AllowAnonymous();
        app.MapOpenApi().AllowAnonymous();
        return app;
    }

    private static bool IsInfrastructurePath(PathString path) =>
        path.StartsWithSegments(LivenessPath) || path.StartsWithSegments(ReadinessPath) || path.StartsWithSegments(MetricsPath);
}
