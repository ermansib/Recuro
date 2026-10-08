using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;
using Recuro.Offer.Api.Endpoints;
using Recuro.Offer.Api.Http;
using Recuro.Offer.Application;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Infrastructure;
using Recuro.Offer.Infrastructure.Clients;
using Recuro.Offer.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("offer");
builder.Services
    .AddOfferApplication()
    .AddOfferInfrastructure(builder.Configuration)
    .AddOfferPolicies()
    .AddExceptionHandler<DependencyExceptionHandler>()
    .AddTransient<ForwardIdentityHandler>();

AddTypedClient<ConfigClient>(builder.Services, e => e.Config, nameof(ServiceEndpoints.Config));
builder.Services.AddTransient<IOfferRulesSource>(sp => sp.GetRequiredService<ConfigClient>());
builder.Services.AddTransient<IWorkingDays>(sp => sp.GetRequiredService<ConfigClient>());
AddClient<IRequisitions, RequisitionClient>(builder.Services, e => e.Requisition, nameof(ServiceEndpoints.Requisition));
AddClient<IMaskingMaps, IdentityMaskingMaps>(builder.Services, e => e.Identity, nameof(ServiceEndpoints.Identity));
AddClient<IWorkflowClient, WorkflowHttpClient>(builder.Services, e => e.Workflow, nameof(ServiceEndpoints.Workflow));

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapOfferEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<OfferDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

static void AddClient<TClient, TImplementation>(IServiceCollection services, Func<ServiceEndpoints, Uri?> address, string name)
    where TClient : class
    where TImplementation : class, TClient =>
    Handlers(services.AddHttpClient<TClient, TImplementation>((sp, client) => Configure(sp, client, address, name)));

static void AddTypedClient<TClient>(IServiceCollection services, Func<ServiceEndpoints, Uri?> address, string name)
    where TClient : class =>
    Handlers(services.AddHttpClient<TClient>((sp, client) => Configure(sp, client, address, name)));

static void Configure(IServiceProvider sp, HttpClient client, Func<ServiceEndpoints, Uri?> address, string name)
{
    var endpoints = sp.GetRequiredService<IOptions<ServiceEndpoints>>().Value;
    endpoints.Apply(client, address(endpoints), name);
}

static void Handlers(IHttpClientBuilder client) =>
    client.AddHttpMessageHandler<CorrelationHeadersHandler>()
        .AddHttpMessageHandler<ForwardIdentityHandler>()
        .AddHttpMessageHandler<ServiceTokenHandler>();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
