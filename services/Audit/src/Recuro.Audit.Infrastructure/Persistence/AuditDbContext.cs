using Microsoft.EntityFrameworkCore;
using Recuro.Audit.Domain.Entries;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.Audit.Infrastructure.Persistence;

/// <summary>The audit service's own database (recuro_audit). No other service reads it.</summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    public DbSet<AuditSeal> Seals => Set<AuditSeal>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditDbContext).Assembly);
}
