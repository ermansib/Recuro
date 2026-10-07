using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Middleware;
using Recuro.Candidate.Api.Endpoints;
using Recuro.Candidate.Api.Http;
using Recuro.Candidate.Application;
using Recuro.Candidate.Infrastructure;
using Recuro.Candidate.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("candidate");
builder.Services
    .AddCandidateApplication()
    .AddCandidateInfrastructure(builder.Configuration)
    .AddCandidatePolicies()
    .AddTransient<ForwardCallerHandler>();
builder.Services.AddIdentityMaskingMaps()
    .AddHttpMessageHandler<ForwardCallerHandler>()
    .AddHttpMessageHandler<CorrelationHeadersHandler>();
builder.Services.AddVendorDirectory(builder.Configuration)
    .AddHttpMessageHandler<ForwardCallerHandler>()
    .AddHttpMessageHandler<CorrelationHeadersHandler>();

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapCandidateEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CandidateDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
