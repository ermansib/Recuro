using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;
using Recuro.Careers.Api.Endpoints;
using Recuro.Careers.Api.Http;
using Recuro.Careers.Application;
using Recuro.Careers.Infrastructure;
using Recuro.Careers.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("careers");
builder.Services
    .AddCareersApplication()
    .AddCareersInfrastructure(builder.Configuration)
    .AddCareersPolicies()
    .AddExceptionHandler<DependencyExceptionHandler>()
    .AddTransient<ForwardCallerHandler>()
    .AddTransient<ServiceCallerHandler>()
    .AddOptions<RecuroAuthOptions>().BindConfiguration(RecuroAuthOptions.SectionName);

// Intake calls are made as this service (the applicant has no token); calendar calls as the HR user.
builder.Services.AddCandidateIntake()
    .AddHttpMessageHandler<ServiceCallerHandler>()
    .AddHttpMessageHandler<CorrelationHeadersHandler>();
builder.Services.AddPipelineIntake()
    .AddHttpMessageHandler<ServiceCallerHandler>()
    .AddHttpMessageHandler<CorrelationHeadersHandler>();
builder.Services.AddWorkingDayCalendar()
    .AddHttpMessageHandler<ForwardCallerHandler>()
    .AddHttpMessageHandler<CorrelationHeadersHandler>();

var app = builder.Build();

app.UsePublicIdempotency();
app.UseRecuroServiceDefaults();
app.MapCareersEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CareersDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
