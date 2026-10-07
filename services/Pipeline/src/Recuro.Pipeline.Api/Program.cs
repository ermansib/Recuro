using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Middleware;
using Recuro.Pipeline.Api.Endpoints;
using Recuro.Pipeline.Api.Http;
using Recuro.Pipeline.Application;
using Recuro.Pipeline.Infrastructure;
using Recuro.Pipeline.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("pipeline");
builder.Services
    .AddPipelineApplication()
    .AddPipelineInfrastructure(builder.Configuration)
    .AddPipelinePolicies()
    .AddExceptionHandler<ConcurrencyExceptionHandler>()
    .AddTransient<ForwardCallerHandler>();
builder.Services.AddCandidateDirectory()
    .AddHttpMessageHandler<ForwardCallerHandler>()
    .AddHttpMessageHandler<CorrelationHeadersHandler>();
builder.Services.AddConfigRules()
    .AddHttpMessageHandler<ForwardCallerHandler>()
    .AddHttpMessageHandler<CorrelationHeadersHandler>();

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapPipelineEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PipelineDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
