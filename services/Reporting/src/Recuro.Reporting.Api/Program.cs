using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;
using Recuro.Reporting.Api.Endpoints;
using Recuro.Reporting.Api.Http;
using Recuro.Reporting.Application;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Infrastructure;
using Recuro.Reporting.Infrastructure.Clients;
using Recuro.Reporting.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("reporting");
builder.Services
    .AddReportingApplication()
    .AddReportingInfrastructure(builder.Configuration)
    .AddReportingPolicies()
    .AddTransient<ForwardIdentityHandler>();

AddClient<ITatTargetsSource, ConfigTatTargets>(builder.Services, e => e.Config, nameof(ServiceEndpoints.Config));
AddClient<IMaskingMaps, IdentityMaskingMaps>(builder.Services, e => e.Identity, nameof(ServiceEndpoints.Identity));

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapReportingEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

// Calls made for a user carry that user's token (ForwardIdentityHandler); calls from the consumer or
// the schedule have no user, so ServiceTokenHandler signs them as recuro-svc-reporting (RCU-AUT-005).
static void AddClient<TClient, TImplementation>(IServiceCollection services, Func<ServiceEndpoints, Uri?> address, string name)
    where TClient : class
    where TImplementation : class, TClient =>
    services.AddHttpClient<TClient, TImplementation>((sp, client) =>
        {
            var endpoints = sp.GetRequiredService<IOptions<ServiceEndpoints>>().Value;
            endpoints.Apply(client, address(endpoints), name);
        })
        .AddHttpMessageHandler<CorrelationHeadersHandler>()
        .AddHttpMessageHandler<ForwardIdentityHandler>()
        .AddHttpMessageHandler<ServiceTokenHandler>();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
