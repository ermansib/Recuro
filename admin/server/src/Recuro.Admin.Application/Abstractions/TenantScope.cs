using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Application.Abstractions;

internal static class TenantScope
{
    public static readonly Error NoTenant =
        Error.Forbidden("tenant.missing", "This action needs a tenant administrator signed in to a tenant.");

    /// <summary>The current tenant id, or a 403 error when the caller is not acting for a tenant.</summary>
    public static Result<Guid> Require(ITenantContext context) =>
        context.TenantId is { } id ? id : NoTenant;

    /// <summary>
    /// The tenant a request acts on: a tenant admin always gets their own tenant; a platform admin names one
    /// (<paramref name="requested"/>), which must exist.
    /// </summary>
    public static async Task<Result<Guid>> ResolveAsync(
        ITenantContext context, ITenantRepository tenants, Guid? requested, CancellationToken ct)
    {
        if (context.TenantId is { } own)
        {
            return requested is null || requested == own ? own : NoTenant;
        }

        if (requested is not { } id)
        {
            return NoTenant;
        }

        return await tenants.GetByIdAsync(id, ct) is null ? TenantErrors.NotFound(id) : id;
    }
}
