using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Recuro.Admin.Api.Auth;
using Recuro.Admin.Api.Endpoints;
using Recuro.Admin.Application;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Infrastructure;
using Recuro.Admin.Infrastructure.Persistence;
using Recuro.Admin.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAdminAuth(builder.Configuration, builder.Environment);

var app = builder.Build();

await PrepareDatabaseAsync(app);

app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// The React client is built into wwwroot and served by this same process (one deployable).
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapPlatformEndpoints();
app.MapTenantEndpoints();
app.MapRuntimeEndpoints();
if (string.Equals(app.Configuration["Auth:Mode"], "Development", StringComparison.OrdinalIgnoreCase))
{
    app.MapDevelopmentEndpoints();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapFallbackToFile("index.html");

await app.RunAsync();

static async Task PrepareDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
    if (db.Database.IsRelational() && db.Database.ProviderName!.Contains("Sqlite", StringComparison.Ordinal))
    {
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }

    var seedDemo = app.Configuration.GetValue<bool>("Database:SeedDemoTenants");
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(seedDemo, CancellationToken.None);
}

/// <summary>Entry point, public so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
