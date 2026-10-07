namespace Recuro.Admin.Domain.Common;

/// <summary>Base for entities identified by a Guid.</summary>
public abstract class Entity
{
    protected Entity(Guid id) => Id = id;

    protected Entity()
    {
    }

    public Guid Id { get; protected set; }
}

/// <summary>Marks an entity owned by one tenant. Persistence filters and stamps <see cref="TenantId"/>.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
