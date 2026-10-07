using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Pipeline.Domain.Requisitions;
using ApplicationEntity = Recuro.Pipeline.Domain.Applications.Application;

namespace Recuro.Pipeline.Infrastructure.Persistence;

/// <summary>The pipeline service's own database (recuro_pipeline). No other service reads it.</summary>
public sealed class PipelineDbContext(DbContextOptions<PipelineDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<ApplicationEntity> Applications => Set<ApplicationEntity>();

    public DbSet<SourcingGate> SourcingGates => Set<SourcingGate>();

    internal DbSet<ApplicationCounter> ApplicationCounters => Set<ApplicationCounter>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PipelineDbContext).Assembly);
}
