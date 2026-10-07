using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Web;
using Recuro.Identity.Api.Endpoints;
using Recuro.Identity.Application;
using Recuro.Identity.Infrastructure;
using Recuro.Identity.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("identity");
builder.Services
    .AddIdentityApplication()
    .AddIdentityInfrastructure(builder.Configuration)
    .AddIdentityPolicies();

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapIdentityEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
