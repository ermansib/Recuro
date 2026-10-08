using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;
using Recuro.Notification.Api.Endpoints;
using Recuro.Notification.Application;
using Recuro.Notification.Infrastructure;
using Recuro.Notification.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("notification");
builder.Services
    .AddNotificationApplication()
    .AddNotificationInfrastructure(builder.Configuration)
    .AddNotificationPolicies();
// Event handlers have no caller to forward, so they call Candidate and Identity as this service (RCU-AUT-005).
builder.Services.AddCandidateContacts()
    .AddHttpMessageHandler<ServiceTokenHandler>()
    .AddHttpMessageHandler<CorrelationHeadersHandler>();
builder.Services.AddStaffDirectory()
    .AddHttpMessageHandler<ServiceTokenHandler>()
    .AddHttpMessageHandler<CorrelationHeadersHandler>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<RememberContactFilter>();
builder.Services.AddOptions<NotificationStreamOptions>().BindConfiguration(NotificationStreamOptions.SectionName);

var app = builder.Build();

app.UseRecuroServiceDefaults();
app.MapNotificationEndpoints();
app.MapNotificationStream();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
