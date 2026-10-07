namespace Recuro.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Scoped, settable implementation of the per-scope context interfaces. The web middleware fills it
/// from the token; the event consumer fills it from the CloudEvent envelope.
/// </summary>
public sealed class ScopeContext : ITenantContext, ICurrentUser, ICorrelationContext
{
    public Guid? TenantId { get; private set; }

    public string? UserId { get; private set; }

    public string? Name { get; private set; }

    public IReadOnlyCollection<string> Roles { get; private set; } = [];

    public string? RequestId { get; private set; }

    public string? CorrelationId { get; private set; }

    public void SetTenant(Guid? tenantId) => TenantId = tenantId;

    public void SetUser(string? userId, string? name, IReadOnlyCollection<string> roles)
    {
        UserId = userId;
        Name = name;
        Roles = roles;
    }

    public void SetCorrelation(string? requestId, string? correlationId)
    {
        RequestId = requestId;
        CorrelationId = correlationId;
    }
}
