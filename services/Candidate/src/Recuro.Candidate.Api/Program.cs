using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Web;
using Recuro.Candidate.Api.Endpoints;
using Recuro.Candidate.Application;
using Recuro.Candidate.Infrastructure;
using Recuro.Candidate.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("candidate");
builder.Services
    .AddCandidateApplication()
    .AddCandidateInfrastructure(builder.Configuration)
    .AddCandidatePolicies();

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
