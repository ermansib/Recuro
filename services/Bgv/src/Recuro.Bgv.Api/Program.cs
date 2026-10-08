using Microsoft.EntityFrameworkCore;
using Recuro.Bgv.Api.Endpoints;
using Recuro.Bgv.Api.Http;
using Recuro.Bgv.Application;
using Recuro.Bgv.Infrastructure;
using Recuro.Bgv.Infrastructure.Persistence;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("bgv");
builder.Services
    .AddBgvApplication()
    .AddBgvInfrastructure(builder.Configuration)
    .AddBgvPolicies()
    .AddExceptionHandler<DependencyExceptionHandler>()
    .AddTransient<ForwardCallerHandler>();

// Calls made for a request carry the caller's own credentials, so Config, Vendor, Workflow and Identity
// authorise and mask for the real caller. Calls with no caller (event handlers) are signed as the bgv
// service account (RCU-AUT-005); ServiceTokenHandler leaves forwarded calls alone, so it runs after.
foreach (var client in new[]
{
    builder.Services.AddConfigRules(),
    builder.Services.AddVendorDirectory(),
    builder.Services.AddWorkflowClient(),
    builder.Services.AddSensitiveNotePolicy(),
})
{
    client
        .AddHttpMessageHandler<ForwardCallerHandler>()
        .AddHttpMessageHandler<ServiceTokenHandler>()
        .AddHttpMessageHandler<CorrelationHeadersHandler>();
}

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapBgvEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<BgvDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
