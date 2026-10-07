using Microsoft.EntityFrameworkCore;
using Recuro.Audit.Api.Endpoints;
using Recuro.Audit.Application;
using Recuro.Audit.Infrastructure;
using Recuro.Audit.Infrastructure.Persistence;
using Recuro.BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("audit");
builder.Services
    .AddAuditApplication()
    .AddAuditInfrastructure(builder.Configuration)
    .AddAuditPolicies();

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapAuditEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
