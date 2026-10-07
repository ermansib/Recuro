using Recuro.BuildingBlocks.Web;
using Recuro.Gateway;
using Recuro.Gateway.Bff;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("gateway");
builder.Services.AddGatewayRateLimits(builder.Configuration);
builder.Services.AddDashboardBff();
builder.Services.AddLogCandidateBff();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transforms =>
    {
        // Services read identity from the forwarded bearer token, never from headers a client could set.
        transforms.AddRequestHeaderRemove("X-User-Id");
        transforms.AddRequestHeaderRemove("X-User-Roles");
        transforms.AddRequestHeaderRemove("X-Tenant-Id");
    });

var app = builder.Build();

// RCU-GTW-001: the fallback policy rejects unauthenticated calls (401) before they reach a service;
// routes opt out with AuthorizationPolicy "anonymous" (public careers endpoints).
app.UseRecuroServiceDefaults(idempotency: false);
app.UseRateLimiter();
app.MapDashboardBff();
app.MapLogCandidateBff();
app.MapReverseProxy();

await app.RunAsync();

/// <summary>Entry point, public so tests can host the gateway.</summary>
public partial class Program;
