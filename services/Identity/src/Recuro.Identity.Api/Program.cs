using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;
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

// Workspace roles (admin API) are read as the identity service account, never with the caller's token:
// they are the same for everyone in the workspace and the admin API only serves them to services.
builder.Services.AddWorkspaceAccess()
    .AddHttpMessageHandler<ServiceTokenHandler>()
    .AddHttpMessageHandler<CorrelationHeadersHandler>();

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
