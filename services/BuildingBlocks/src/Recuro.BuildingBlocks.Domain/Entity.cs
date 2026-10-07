namespace Recuro.BuildingBlocks.Domain;

/// <summary>Base for entities identified by a Guid.</summary>
public abstract class Entity
{
    protected Entity(Guid id) => Id = id;

    protected Entity()
    {
    }

    public Guid Id { get; protected set; }
}

/// <summary>Something that happened inside one aggregate. Handled in the same transaction.</summary>
public interface IDomainEvent;

/// <summary>Consistency boundary. Collects domain events that persistence dispatches before commit.</summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(Guid id)
        : base(id)
    {
    }

    protected AggregateRoot()
    {
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}

/// <summary>
/// Marks a row owned by one tenant. Persistence filters every query by the current tenant and stamps
/// <see cref="TenantId"/> on insert, so no handler has to remember to.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
