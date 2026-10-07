using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Middleware;
using Recuro.Requisition.Api.Endpoints;
using Recuro.Requisition.Api.Http;
using Recuro.Requisition.Application;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Infrastructure;
using Recuro.Requisition.Infrastructure.Clients;
using Recuro.Requisition.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("requisition");
builder.Services
    .AddRequisitionApplication()
    .AddRequisitionInfrastructure(builder.Configuration)
    .AddRequisitionPolicies()
    .AddExceptionHandler<DependencyExceptionHandler>()
    .AddTransient<ForwardIdentityHandler>();

builder.Services.AddHttpClient<IRulesClient, ConfigRulesClient>((sp, client) =>
        Endpoints(sp).Apply(client, Endpoints(sp).Config, nameof(ServiceEndpoints.Config)))
    .AddHttpMessageHandler<CorrelationHeadersHandler>()
    .AddHttpMessageHandler<ForwardIdentityHandler>();
builder.Services.AddHttpClient<IWorkflowClient, WorkflowHttpClient>((sp, client) =>
        Endpoints(sp).Apply(client, Endpoints(sp).Workflow, nameof(ServiceEndpoints.Workflow)))
    .AddHttpMessageHandler<CorrelationHeadersHandler>()
    .AddHttpMessageHandler<ForwardIdentityHandler>();

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapRequisitionEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<RequisitionDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

static ServiceEndpoints Endpoints(IServiceProvider services) => services.GetRequiredService<IOptions<ServiceEndpoints>>().Value;

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
