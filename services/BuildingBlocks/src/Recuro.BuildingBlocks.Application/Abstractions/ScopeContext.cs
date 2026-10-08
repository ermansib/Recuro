namespace Recuro.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Scoped, settable implementation of the per-scope context interfaces. The web middleware fills it
/// from the token; the event consumer fills it from the CloudEvent envelope.
/// </summary>
public sealed class ScopeContext : ITenantContext, ICurrentUser, ICorrelationContext
{
    private static readonly AsyncLocal<ScopeContext?> CurrentScope = new();

    /// <summary>
    /// The scope of the request or event being handled on this async flow. For code that DI can't hand the
    /// scoped context to, such as HttpClient message handlers, which run in their own DI scope.
    /// </summary>
    public static ScopeContext? Current => CurrentScope.Value;

    public Guid? TenantId { get; private set; }

    public string? UserId { get; private set; }

    public string? Name { get; private set; }

    public IReadOnlyCollection<string> Roles { get; private set; } = [];

    public string? RequestId { get; private set; }

    public string? CorrelationId { get; private set; }

    public void SetTenant(Guid? tenantId) => TenantId = tenantId;

    /// <summary>Makes this the <see cref="Current"/> scope for the rest of the calling async flow.</summary>
    public void MakeCurrent() => CurrentScope.Value = this;

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
