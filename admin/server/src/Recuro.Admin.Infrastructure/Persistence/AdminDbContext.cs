using Microsoft.EntityFrameworkCore;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Screens;
using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Infrastructure.Persistence;

public sealed class AdminDbContext(DbContextOptions<AdminDbContext> options, ITenantContext tenantContext)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<ThemePreset> ThemePresets => Set<ThemePreset>();

    public DbSet<ScreenDefinition> ScreenDefinitions => Set<ScreenDefinition>();

    public DbSet<TenantScreenConfiguration> TenantScreenConfigurations => Set<TenantScreenConfiguration>();

    /// <summary>Read by the query filter on every query, so it follows the current request's tenant.</summary>
    private Guid? CurrentTenantId => tenantContext.TenantId;

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken ct) => await SaveChangesAsync(ct);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdminDbContext).Assembly);

        // Tenant isolation: tenant-owned rows are only visible to the tenant they belong to.
        // Repositories that take an explicit tenant id (for example the public runtime config) opt out deliberately.
        modelBuilder.Entity<TenantScreenConfiguration>().HasQueryFilter(c => c.TenantId == CurrentTenantId);
    }

    /// <summary>A tenant admin can never write another tenant's rows, even if a handler is wrong.</summary>
    private void GuardTenantWrites()
    {
        if (CurrentTenantId is not { } tenantId)
        {
            return;
        }

        var foreign = ChangeTracker.Entries<ITenantOwned>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Any(e => e.Entity.TenantId != tenantId);

        if (foreign)
        {
            throw new InvalidOperationException("Attempted to write data owned by another tenant.");
        }
    }
}
