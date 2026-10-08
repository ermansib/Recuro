using Microsoft.EntityFrameworkCore;
using Recuro.Bgv.Domain.Cases;
using Recuro.Bgv.Domain.Requests;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.Bgv.Infrastructure.Persistence;

/// <summary>The BGV service's own database (recuro_bgv). No other service reads it.</summary>
public sealed class BgvDbContext(DbContextOptions<BgvDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<BgvCase> Cases => Set<BgvCase>();

    public DbSet<BgvRequest> Requests => Set<BgvRequest>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BgvDbContext).Assembly);
}
