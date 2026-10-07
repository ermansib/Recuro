using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Domain;
using Recuro.BuildingBlocks.Infrastructure.Inbox;
using Recuro.BuildingBlocks.Infrastructure.Outbox;

namespace Recuro.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Base DbContext for every service. It owns the rules a service must not forget:
/// <list type="bullet">
/// <item>every <see cref="ITenantOwned"/> entity gets a query filter on the current tenant and an index on TenantId;</item>
/// <item>new tenant-owned rows are stamped with the current tenant; writing another tenant's row throws;</item>
/// <item>domain events are dispatched before commit, so their side effects (outbox rows) share the transaction;</item>
/// <item>the outbox and inbox tables live in the service's own database.</item>
/// </list>
/// </summary>
public abstract class RecuroDbContext : DbContext, IUnitOfWork
{
    private const string TenantIdProperty = nameof(ITenantOwned.TenantId);
    private readonly ITenantContext _tenant;
    private readonly IDomainEventDispatcher _domainEvents;

    protected RecuroDbContext(DbContextOptions options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
        : base(options)
    {
        _tenant = tenant;
        _domainEvents = domainEvents;
    }

    /// <summary>
    /// The tenant every query is filtered by. With no tenant in scope it is <see cref="Guid.Empty"/>, which
    /// matches nothing: a missing tenant fails closed.
    /// </summary>
    protected Guid CurrentTenantId => _tenant.TenantId ?? Guid.Empty;

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await DispatchDomainEventsAsync(cancellationToken);
        StampAndGuardTenant();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Synchronous saves would skip async domain-event handlers, so they are not allowed.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync.");

    /// <summary>Configure the service's own entities here.</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        ApplyTenantFilters(modelBuilder);
    }

    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        var tenantOwned = modelBuilder.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && !t.IsOwned() && typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .ToList();

        foreach (var entityType in tenantOwned)
        {
            // e => EF.Property<Guid>(e, "TenantId") == this.CurrentTenantId
            // Referencing the context instance makes EF re-read the tenant on every query.
            var entity = Expression.Parameter(entityType.ClrType, "e");
            var tenantId = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(Guid)], entity, Expression.Constant(TenantIdProperty));
            var current = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
            var filter = Expression.Lambda(Expression.Equal(tenantId, current), entity);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            modelBuilder.Entity(entityType.ClrType).HasIndex(TenantIdProperty);
        }
    }

    private async Task DispatchDomainEventsAsync(CancellationToken ct)
    {
        // Handlers may change other aggregates and raise more events, so drain until quiet.
        for (var round = 0; round < 10; round++)
        {
            var aggregates = ChangeTracker.Entries<AggregateRoot>()
                .Select(e => e.Entity)
                .Where(a => a.DomainEvents.Count > 0)
                .ToList();
            if (aggregates.Count == 0)
            {
                return;
            }

            var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
            aggregates.ForEach(a => a.ClearDomainEvents());
            foreach (var domainEvent in events)
            {
                await _domainEvents.DispatchAsync(domainEvent, ct);
            }
        }

        throw new InvalidOperationException("Domain event handlers kept raising events after 10 rounds.");
    }

    private void StampAndGuardTenant()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.Entity is ITenantOwned))
        {
            if (entry.State == EntityState.Added)
            {
                StampNewRow(entry);
            }
            else if (entry.State == EntityState.Modified && entry.Property(TenantIdProperty).IsModified)
            {
                throw new InvalidOperationException($"{entry.Metadata.ClrType.Name}.TenantId cannot change.");
            }
        }
    }

    private void StampNewRow(EntityEntry entry)
    {
        var property = entry.Property(TenantIdProperty);
        var current = _tenant.RequiredTenantId;
        var value = (Guid)property.CurrentValue!;
        if (value == Guid.Empty)
        {
            property.CurrentValue = current;
        }
        else if (value != current)
        {
            throw new InvalidOperationException($"Cannot write a {entry.Metadata.ClrType.Name} for another tenant.");
        }
    }
}
