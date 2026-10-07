using Recuro.Admin.Application.Abstractions;

namespace Recuro.Admin.Api.Auth;

/// <summary>Resolves the tenant once per request from the signed-in user's tenant claim.</summary>
internal sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid? TenantId =>
        accessor.HttpContext?.User.FindFirst(AdminClaims.Tenant)?.Value is { } value && Guid.TryParse(value, out var id)
            ? id
            : null;
}
