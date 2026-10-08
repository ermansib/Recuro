using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;
using Recuro.Interview.Api.Endpoints;
using Recuro.Interview.Api.Http;
using Recuro.Interview.Application;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Infrastructure;
using Recuro.Interview.Infrastructure.Clients;
using Recuro.Interview.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("interview");
builder.Services
    .AddInterviewApplication()
    .AddInterviewInfrastructure(builder.Configuration)
    .AddInterviewPolicies()
    .AddExceptionHandler<DependencyExceptionHandler>()
    .AddTransient<ForwardIdentityHandler>();

AddClient<IInterviewRulesSource, ConfigInterviewRules>(builder.Services, e => e.Config, nameof(ServiceEndpoints.Config));
AddClient<IJobDescriptions, RequisitionJobDescriptions>(builder.Services, e => e.Requisition, nameof(ServiceEndpoints.Requisition));
AddClient<IAccessDecisions, IdentityAccessDecisions>(builder.Services, e => e.Identity, nameof(ServiceEndpoints.Identity));
AddClient<IWorkflowClient, WorkflowHttpClient>(builder.Services, e => e.Workflow, nameof(ServiceEndpoints.Workflow));

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapInterviewEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<InterviewDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

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
