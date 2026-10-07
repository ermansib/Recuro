using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Identity.Domain.Users;

namespace Recuro.Identity.Infrastructure.Persistence;

/// <summary>The identity service's own database (recuro_identity). No other service reads it.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
}
