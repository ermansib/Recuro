using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Reporting.Domain.Events;
using Recuro.Reporting.Domain.Kpis;
using Recuro.Reporting.Domain.Projections;
using Recuro.Reporting.Domain.Reports;

namespace Recuro.Reporting.Infrastructure.Persistence;

/// <summary>
/// The Reporting service's own database (recuro_reporting): the event log, the projections folded from
/// it, and what is built on them. No other service reads it, and it never reads theirs (RCU-RPT-001).
/// </summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<ReportEvent> Events => Set<ReportEvent>();

    public DbSet<ProjectionCheckpoint> Checkpoints => Set<ProjectionCheckpoint>();

    public DbSet<ApplicationFact> Applications => Set<ApplicationFact>();

    public DbSet<RequisitionFact> Requisitions => Set<RequisitionFact>();

    public DbSet<FeedbackFact> Feedback => Set<FeedbackFact>();

    public DbSet<RecruitmentCost> Costs => Set<RecruitmentCost>();

    public DbSet<MetricDefinitionSet> DefinitionSets => Set<MetricDefinitionSet>();

    public DbSet<ReportSettings> Settings => Set<ReportSettings>();

    public DbSet<KpiSnapshot> Snapshots => Set<KpiSnapshot>();

    public DbSet<ReportPack> Packs => Set<ReportPack>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReportingDbContext).Assembly);
}

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ReportingDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_reporting", typeof(ReportingDbContext).Assembly.GetName().Name!);
        return new ReportingDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance);
    }
}
