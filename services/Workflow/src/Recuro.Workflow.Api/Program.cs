using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Middleware;
using Recuro.Workflow.Api.Endpoints;
using Recuro.Workflow.Api.Http;
using Recuro.Workflow.Application;
using Recuro.Workflow.Application.Abstractions;
using Recuro.Workflow.Infrastructure;
using Recuro.Workflow.Infrastructure.Clients;
using Recuro.Workflow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("workflow");
builder.Services
    .AddWorkflowApplication()
    .AddWorkflowInfrastructure(builder.Configuration)
    .AddWorkflowPolicies()
    .AddExceptionHandler<DependencyExceptionHandler>()
    .AddTransient<ForwardIdentityHandler>();

builder.Services.AddHttpClient<IBusinessCalendar, ConfigCalendarClient>((sp, client) =>
    {
        var endpoints = sp.GetRequiredService<IOptions<ServiceEndpoints>>().Value;
        endpoints.Apply(client, endpoints.Config, nameof(ServiceEndpoints.Config));
    })
    .AddHttpMessageHandler<CorrelationHeadersHandler>()
    .AddHttpMessageHandler<ForwardIdentityHandler>();

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapWorkflowEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
