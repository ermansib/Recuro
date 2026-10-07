using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Infrastructure.Persistence;

/// <summary>The config service's own database (recuro_config). No other service reads it.</summary>
public sealed class ConfigDbContext(DbContextOptions<ConfigDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<RuleSetVersion> Versions => Set<RuleSetVersion>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConfigDbContext).Assembly);
}
