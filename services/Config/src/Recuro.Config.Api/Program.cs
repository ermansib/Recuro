using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web;
using Recuro.Config.Api.Endpoints;
using Recuro.Config.Application;
using Recuro.Config.Application.RuleSets.Commands;
using Recuro.Config.Infrastructure;
using Recuro.Config.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("config");
builder.Services
    .AddConfigApplication()
    .AddConfigInfrastructure(builder.Configuration)
    .AddConfigPolicies();

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapConfigEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ConfigDbContext>().Database.MigrateAsync();
}

// Local tenants get the FRD §5 matrices as version 1 so the resolve API works out of the box.
foreach (var tenantId in app.Configuration.GetSection("Config:SeedTenants").Get<Guid[]>() ?? [])
{
    await using var scope = app.Services.CreateAsyncScope();
    scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenantId);
    await scope.ServiceProvider.GetRequiredService<ICommandHandler<SeedDefaultRuleSetsCommand, IReadOnlyList<string>>>()
        .Handle(new SeedDefaultRuleSetsCommand(), CancellationToken.None);
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
