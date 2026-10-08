using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;
using Recuro.Onboarding.Api.Endpoints;
using Recuro.Onboarding.Api.Http;
using Recuro.Onboarding.Application;
using Recuro.Onboarding.Infrastructure;
using Recuro.Onboarding.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("onboarding");
builder.Services
    .AddOnboardingApplication()
    .AddOnboardingInfrastructure(builder.Configuration)
    .AddOnboardingPolicies()
    .AddExceptionHandler<DependencyExceptionHandler>()
    .AddTransient<ForwardCallerHandler>();

// Calls made for a request carry the caller's own credentials, so Config and Candidate authorise and
// mask for the real caller. Calls with no caller (event handlers, the scheduler) are signed as the
// onboarding service account (RCU-AUT-005); ServiceTokenHandler leaves forwarded calls alone, so it runs after.
var (config, candidate) = builder.Services.AddOnboardingClients();
foreach (var client in new[] { config, candidate })
{
    client
        .AddHttpMessageHandler<ForwardCallerHandler>()
        .AddHttpMessageHandler<ServiceTokenHandler>()
        .AddHttpMessageHandler<CorrelationHeadersHandler>();
}

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapOnboardingEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<OnboardingDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
