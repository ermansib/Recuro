using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Web;
using Recuro.Vendor.Api.Endpoints;
using Recuro.Vendor.Application;
using Recuro.Vendor.Infrastructure;
using Recuro.Vendor.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("vendor");
builder.Services
    .AddVendorApplication()
    .AddVendorInfrastructure(builder.Configuration)
    .AddVendorPolicies();

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapVendorEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<VendorDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
