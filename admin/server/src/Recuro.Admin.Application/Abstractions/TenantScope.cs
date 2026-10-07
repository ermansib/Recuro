using Recuro.Admin.Domain.Common;

namespace Recuro.Admin.Application.Abstractions;

internal static class TenantScope
{
    public static readonly Error NoTenant =
        Error.Forbidden("tenant.missing", "This action needs a tenant administrator signed in to a tenant.");

    /// <summary>The current tenant id, or a 403 error when the caller is not acting for a tenant.</summary>
    public static Result<Guid> Require(ITenantContext context) =>
        context.TenantId is { } id ? id : NoTenant;
}
