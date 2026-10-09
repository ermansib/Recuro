using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;
using Recuro.Reporting.Application.Projections;
using Recuro.Reporting.Application.Reports;
using Recuro.Reporting.Application.Reports.Commands;

namespace Recuro.Reporting.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddReportingApplication(this IServiceCollection services)
    {
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
        services.AddScoped<ReportProjector>();
        services.AddScoped<IReportCalendar, ReportCalendar>();
        services.AddScoped<IKpiReportBuilder, KpiReportBuilder>();
        services.AddScoped<IReportMask, ReportMask>();
        services.AddScoped<ISnapshotFreezer, SnapshotFreezer>();
        services.AddScoped<IPackBuilder, PackBuilder>();
        return services;
    }
}
