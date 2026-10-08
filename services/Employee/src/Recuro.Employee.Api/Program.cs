using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Web;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;
using Recuro.Employee.Api.Endpoints;
using Recuro.Employee.Api.Http;
using Recuro.Employee.Application;
using Recuro.Employee.Infrastructure;
using Recuro.Employee.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRecuroServiceDefaults("employee");
builder.Services
    .AddEmployeeApplication()
    .AddEmployeeInfrastructure(builder.Configuration)
    .AddEmployeePolicies()
    .AddExceptionHandler<DependencyExceptionHandler>()
    .AddTransient<ForwardCallerHandler>()
    .AddTransient<ServiceCallerHandler>()
    .AddOptions<RecuroAuthOptions>().BindConfiguration(RecuroAuthOptions.SectionName);

// Intake calls are made as this service (employees may not create candidates); calendar calls as the HR user.
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

app.UseRecuroServiceDefaults();
app.MapEmployeeEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, public so integration tests can host the API.</summary>
public partial class Program;
